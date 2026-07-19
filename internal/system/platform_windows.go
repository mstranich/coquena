//go:build windows

package system

import (
	"encoding/binary"
	"fmt"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"unsafe"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
)

type windowsInventory struct{}

func inspectPlatform() (Report, error) {
	return inspectWith(windowsInventory{}, cudaProbe{}, vulkanProbe{}, staticProbe{capability: BackendCapability{Type: "metal", State: Unsupported, Diagnostics: []Diagnostic{{Code: "backend-platform-unsupported", Backend: "metal", Message: "Metal is not supported on Windows"}}}})
}

func (windowsInventory) Detect(report *Report) []Diagnostic {
	diagnostics := []Diagnostic{}
	report.OS.Family = "windows"
	report.OS.Name = "Windows"
	if version := windows.RtlGetVersion(); version != nil {
		report.OS.Version = fmt.Sprintf("%d.%d.%d", version.MajorVersion, version.MinorVersion, version.BuildNumber)
	}
	report.OS.NativeArchitecture = nativeArchitecture()
	report.CPU.Model, report.CPU.Vendor = readCPU()
	if report.CPU.Model == "" {
		diagnostics = append(diagnostics, Diagnostic{Code: "cpu-identity-unknown", Message: "CPU identity could not be determined", Severity: "warning"})
	}
	if total, ok := physicalMemory(); ok {
		report.Memory.TotalBytes = &total
	} else {
		diagnostics = append(diagnostics, Diagnostic{Code: "memory-query-failed", Message: "Total physical memory could not be determined", Severity: "warning"})
	}
	report.Graphics = normalizeGraphics(readGraphics())
	if len(report.Graphics) == 0 {
		diagnostics = append(diagnostics, Diagnostic{Code: "graphics-not-found", Message: "No hardware graphics adapters were detected", Severity: "info"})
	}
	return diagnostics
}

func nativeArchitecture() string {
	kernel := windows.NewLazySystemDLL("kernel32.dll")
	proc := kernel.NewProc("IsWow64Process2")
	var processMachine, nativeMachine uint16
	r1, _, _ := proc.Call(uintptr(windows.CurrentProcess()), uintptr(unsafe.Pointer(&processMachine)), uintptr(unsafe.Pointer(&nativeMachine)))
	if r1 == 0 {
		return runtime.GOARCH
	}
	return architectureFromMachine(nativeMachine)
}

func architectureFromMachine(machine uint16) string {
	switch machine {
	case 0x8664:
		return "amd64"
	case 0xaa64:
		return "arm64"
	case 0x014c:
		return "386"
	}
	return fmt.Sprintf("machine-0x%04x", machine)
}

type memoryStatusEx struct {
	Length                                                                                               uint32
	MemoryLoad                                                                                           uint32
	TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual uint64
}

func physicalMemory() (uint64, bool) {
	status := memoryStatusEx{Length: uint32(unsafe.Sizeof(memoryStatusEx{}))}
	proc := windows.NewLazySystemDLL("kernel32.dll").NewProc("GlobalMemoryStatusEx")
	r1, _, _ := proc.Call(uintptr(unsafe.Pointer(&status)))
	return status.TotalPhys, r1 != 0
}

func readCPU() (string, string) {
	key, err := registry.OpenKey(registry.LOCAL_MACHINE, `HARDWARE\DESCRIPTION\System\CentralProcessor\0`, registry.QUERY_VALUE)
	if err != nil {
		return "", ""
	}
	defer key.Close()
	model, _, _ := key.GetStringValue("ProcessorNameString")
	vendor, _, _ := key.GetStringValue("VendorIdentifier")
	return strings.TrimSpace(model), strings.TrimSpace(vendor)
}

func readGraphics() []GraphicsDevice {
	const classPath = `SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}`
	root, err := registry.OpenKey(registry.LOCAL_MACHINE, classPath, registry.ENUMERATE_SUB_KEYS)
	if err != nil {
		return []GraphicsDevice{}
	}
	defer root.Close()
	names, err := root.ReadSubKeyNames(-1)
	if err != nil {
		return []GraphicsDevice{}
	}
	devices := []GraphicsDevice{}
	for _, name := range names {
		if len(name) != 4 {
			continue
		}
		key, err := registry.OpenKey(root, name, registry.QUERY_VALUE)
		if err != nil {
			continue
		}
		desc, _, _ := key.GetStringValue("DriverDesc")
		matching, _, _ := key.GetStringValue("MatchingDeviceId")
		if desc == "" {
			key.Close()
			continue
		}
		vendorID, deviceID := parsePCI(matching)
		device := GraphicsDevice{ID: "windows-display:" + name, Name: desc, Vendor: vendorName(vendorID), Source: "windows-display-class", VendorID: vendorID, DeviceID: deviceID, Type: "hardware"}
		if data, _, err := key.GetBinaryValue("HardwareInformation.MemorySize"); err == nil && len(data) >= 4 {
			value := uint64(binary.LittleEndian.Uint32(data))
			if value > 0 {
				device.MemoryBytes = &value
			}
		}
		key.Close()
		devices = append(devices, device)
	}
	return devices
}

func normalizeGraphics(devices []GraphicsDevice) []GraphicsDevice {
	result := make([]GraphicsDevice, 0, len(devices))
	for _, device := range devices {
		if device.ID == "" || device.Name == "" || device.Type == "software" {
			continue
		}
		result = append(result, device)
	}
	return result
}

func parsePCI(value string) (*uint32, *uint32) {
	upper := strings.ToUpper(value)
	parse := func(marker string) *uint32 {
		i := strings.Index(upper, marker)
		if i < 0 || len(upper) < i+len(marker)+4 {
			return nil
		}
		n, err := strconv.ParseUint(upper[i+len(marker):i+len(marker)+4], 16, 32)
		if err != nil {
			return nil
		}
		v := uint32(n)
		return &v
	}
	return parse("VEN_"), parse("DEV_")
}

func vendorName(id *uint32) string {
	if id == nil {
		return ""
	}
	switch *id {
	case 0x10de:
		return "nvidia"
	case 0x8086:
		return "intel"
	case 0x1002, 0x1022:
		return "amd"
	}
	return fmt.Sprintf("0x%04x", *id)
}

type staticProbe struct{ capability BackendCapability }

func (p staticProbe) Probe(Report) BackendCapability { return p.capability }

func loadSystemDLL(name string) (windows.Handle, error) {
	return windows.LoadLibraryEx(name, 0, windows.LOAD_LIBRARY_SEARCH_SYSTEM32)
}

var loadOptionalSystemDLL = loadSystemDLL

func procAddress(module windows.Handle, name string) (uintptr, error) {
	return windows.GetProcAddress(module, name)
}

type cudaProbe struct{}

func (cudaProbe) Probe(report Report) BackendCapability {
	capability := BackendCapability{Type: "cuda"}
	module, err := loadOptionalSystemDLL("nvcuda.dll")
	if err != nil {
		return unavailableCapability("cuda", "cuda-driver-not-found", "No NVIDIA CUDA driver was detected")
	}
	defer windows.FreeLibrary(module)
	call := func(name string, args ...uintptr) (uintptr, error) {
		p, e := procAddress(module, name)
		if e != nil {
			return 0, e
		}
		r, _, _ := syscall.SyscallN(p, args...)
		return r, nil
	}
	if result, err := call("cuInit", 0); err != nil || result != 0 {
		return unknownCapability("cuda", "cuda-initialization-failed", "The CUDA driver could not be initialized")
	}
	var driver int32
	if result, err := call("cuDriverGetVersion", uintptr(unsafe.Pointer(&driver))); err != nil || result != 0 {
		return unknownCapability("cuda", "cuda-version-query-failed", "The CUDA driver version could not be determined")
	}
	var count int32
	if result, err := call("cuDeviceGetCount", uintptr(unsafe.Pointer(&count))); err != nil || result != 0 {
		return unknownCapability("cuda", "cuda-device-query-failed", "CUDA devices could not be enumerated")
	}
	if count == 0 {
		return unavailableCapability("cuda", "cuda-device-not-found", "No CUDA-capable device was detected")
	}
	capability.State = Available
	capability.Version = fmt.Sprintf("%d.%d", driver/1000, (driver%1000)/10)
	capability.Evidence = append(capability.Evidence, Evidence{Code: "cuda-driver-version", Value: capability.Version})
	for i := int32(0); i < count; i++ {
		var name [256]byte
		result, err := call("cuDeviceGetName", uintptr(unsafe.Pointer(&name[0])), uintptr(len(name)), uintptr(i))
		if err == nil && result == 0 {
			n := strings.TrimRight(string(name[:]), "\x00")
			capability.Evidence = append(capability.Evidence, Evidence{Code: "cuda-device", Value: n})
		}
	}
	return capability
}

type vulkanProbe struct{}
type vkApplicationInfo struct {
	SType              uint32
	_                  uint32
	PNext              uintptr
	PApplicationName   uintptr
	ApplicationVersion uint32
	_                  uint32
	PEngineName        uintptr
	EngineVersion      uint32
	APIVersion         uint32
}
type vkInstanceCreateInfo struct {
	SType                   uint32
	_                       uint32
	PNext                   uintptr
	Flags                   uint32
	_                       uint32
	PApplicationInfo        uintptr
	EnabledLayerCount       uint32
	_                       uint32
	PPEnabledLayerNames     uintptr
	EnabledExtensionCount   uint32
	_                       uint32
	PPEnabledExtensionNames uintptr
}

func (vulkanProbe) Probe(report Report) BackendCapability {
	module, err := loadOptionalSystemDLL("vulkan-1.dll")
	if err != nil {
		return unavailableCapability("vulkan", "vulkan-loader-not-found", "No Vulkan loader was detected")
	}
	defer windows.FreeLibrary(module)
	get := func(name string) (uintptr, error) { return procAddress(module, name) }
	version := "1.0"
	if p, e := get("vkEnumerateInstanceVersion"); e == nil {
		var v uint32
		r, _, _ := syscall.SyscallN(p, uintptr(unsafe.Pointer(&v)))
		if int32(r) == 0 {
			version = fmt.Sprintf("%d.%d.%d", v>>22, (v>>12)&0x3ff, v&0xfff)
		}
	}
	create, e := get("vkCreateInstance")
	if e != nil {
		return unknownCapability("vulkan", "vulkan-entry-point-missing", "The Vulkan loader lacks required entry points")
	}
	appName := []byte("Coquena\x00")
	app := vkApplicationInfo{SType: 0, PApplicationName: uintptr(unsafe.Pointer(&appName[0])), APIVersion: 1 << 22}
	info := vkInstanceCreateInfo{SType: 1, PApplicationInfo: uintptr(unsafe.Pointer(&app))}
	var instance uintptr
	r, _, _ := syscall.SyscallN(create, uintptr(unsafe.Pointer(&info)), 0, uintptr(unsafe.Pointer(&instance)))
	if int32(r) != 0 {
		return unknownCapability("vulkan", "vulkan-initialization-failed", "The Vulkan loader could not create an instance")
	}
	destroy, _ := get("vkDestroyInstance")
	if destroy != 0 {
		defer syscall.SyscallN(destroy, instance, 0)
	}
	enumerate, e := get("vkEnumeratePhysicalDevices")
	if e != nil {
		return unknownCapability("vulkan", "vulkan-entry-point-missing", "The Vulkan loader cannot enumerate physical devices")
	}
	var count uint32
	r, _, _ = syscall.SyscallN(enumerate, instance, uintptr(unsafe.Pointer(&count)), 0)
	if int32(r) != 0 {
		return unknownCapability("vulkan", "vulkan-device-query-failed", "Vulkan physical devices could not be enumerated")
	}
	if count == 0 {
		return unavailableCapability("vulkan", "vulkan-device-not-found", "No Vulkan physical device was detected")
	}
	devices := make([]uintptr, count)
	r, _, _ = syscall.SyscallN(enumerate, instance, uintptr(unsafe.Pointer(&count)), uintptr(unsafe.Pointer(&devices[0])))
	if int32(r) != 0 {
		return unknownCapability("vulkan", "vulkan-device-query-failed", "Vulkan physical devices could not be enumerated")
	}
	capability := BackendCapability{Type: "vulkan", State: Available, Version: version, Evidence: []Evidence{{Code: "vulkan-instance-version", Value: version}}}
	if properties, e := get("vkGetPhysicalDeviceProperties"); e == nil {
		for _, device := range devices {
			var data [1024]byte
			syscall.SyscallN(properties, device, uintptr(unsafe.Pointer(&data[0])))
			name := strings.TrimRight(string(data[20:276]), "\x00")
			capability.Evidence = append(capability.Evidence, Evidence{Code: "vulkan-device", Value: name})
		}
	}
	return capability
}

func unavailableCapability(backend, code, message string) BackendCapability {
	return BackendCapability{Type: backend, State: Unavailable, Diagnostics: []Diagnostic{{Code: code, Backend: backend, Message: message, Severity: "info"}}}
}
func unknownCapability(backend, code, message string) BackendCapability {
	return BackendCapability{Type: backend, State: Unknown, Diagnostics: []Diagnostic{{Code: code, Backend: backend, Message: message, Severity: "warning"}}}
}
