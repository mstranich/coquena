//go:build !windows

package system

import "runtime"

type deferredProbe struct {
	backend string
	state   State
}

func (p deferredProbe) Probe(Report) BackendCapability {
	return BackendCapability{Type: p.backend, State: p.state, Diagnostics: []Diagnostic{{Code: "probe-not-implemented", Backend: p.backend, Message: "Detailed backend inspection is not implemented on this platform"}}}
}

func inspectPlatform() (Report, error) {
	return inspectWith(nil,
		deferredProbe{backend: "cuda", state: Unknown},
		deferredProbe{backend: "metal", state: map[bool]State{true: Unknown, false: Unsupported}[runtime.GOOS == "darwin"]},
		deferredProbe{backend: "vulkan", state: Unknown},
	)
}
