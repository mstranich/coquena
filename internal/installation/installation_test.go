package installation

import (
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestInitializeCreatesPortableLayoutAndJSON(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))

	result, err := initialize(operationsForExecutable(executable))
	if err != nil {
		t.Fatalf("initialize() error = %v", err)
	}
	if result.Root != root || result.AlreadyInitialized {
		t.Fatalf("initialize() result = %+v, want new root %q", result, root)
	}
	for _, directory := range requiredDirectories {
		assertDirectory(t, filepath.Join(root, directory))
	}
	assertFileContent(t, filepath.Join(root, configDirectory, configFilename), "{\n  \"schemaVersion\": 1\n}\n")
	assertFileContent(t, executable, "binary")
}

func TestInitializeUsesExecutableRootNotWorkingDirectory(t *testing.T) {
	root := t.TempDir()
	working := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))

	previous, err := os.Getwd()
	if err != nil {
		t.Fatal(err)
	}
	if err := os.Chdir(working); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.Chdir(previous) })

	if _, err := initialize(operationsForExecutable(executable)); err != nil {
		t.Fatalf("initialize() error = %v", err)
	}
	assertDirectory(t, filepath.Join(root, "config"))
	if _, err := os.Stat(filepath.Join(working, "config")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("working directory config exists or stat failed unexpectedly: %v", err)
	}
}

func TestResolveRootFollowsExecutableLink(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("creating symbolic links may require privileges on Windows")
	}

	root := t.TempDir()
	launcherRoot := t.TempDir()
	executable := filepath.Join(root, "coquena")
	link := filepath.Join(launcherRoot, "coquena")
	mustWriteFile(t, executable, []byte("binary"))
	if err := os.Symlink(executable, link); err != nil {
		t.Fatal(err)
	}

	resolved, err := resolveRoot(operationsForExecutable(link))
	if err != nil {
		t.Fatalf("resolveRoot() error = %v", err)
	}
	if resolved != root {
		t.Fatalf("resolveRoot() = %q, want %q", resolved, root)
	}
}

func TestResolveHomeUsesAbsoluteEnvironmentOverride(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	op := operationsForExecutable(filepath.Join(t.TempDir(), executableName()))
	op.lookupEnv = func(string) (string, bool) { return root, true }

	got, err := resolveHome(op)
	if err != nil {
		t.Fatal(err)
	}
	if got != root {
		t.Fatalf("resolveHome() = %q, want %q", got, root)
	}
}

func TestResolveHomeRejectsInvalidEnvironmentOverride(t *testing.T) {
	t.Parallel()

	for _, value := range []string{"", "relative/home"} {
		t.Run(value, func(t *testing.T) {
			op := operationsForExecutable(filepath.Join(t.TempDir(), executableName()))
			op.lookupEnv = func(string) (string, bool) { return value, true }
			if _, err := resolveHome(op); err == nil || !strings.Contains(err.Error(), "COQUENA_HOME") {
				t.Fatalf("resolveHome() error = %v, want COQUENA_HOME diagnostic", err)
			}
		})
	}
}

func TestInitializeUsesEnvironmentOverride(t *testing.T) {
	t.Parallel()

	home := t.TempDir()
	executableRoot := t.TempDir()
	op := operationsForExecutable(filepath.Join(executableRoot, executableName()))
	op.lookupEnv = func(string) (string, bool) { return home, true }
	if _, err := initialize(op); err != nil {
		t.Fatal(err)
	}
	assertDirectory(t, filepath.Join(home, "config"))
	if _, err := os.Stat(filepath.Join(executableRoot, "config")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("executable root was initialized or stat failed unexpectedly: %v", err)
	}
}

func TestInitializeIsIdempotentAndCompletesMissingDirectories(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))
	op := operationsForExecutable(executable)
	if _, err := initialize(op); err != nil {
		t.Fatal(err)
	}

	configPath := filepath.Join(root, configDirectory, configFilename)
	custom := []byte("{\n  \"schemaVersion\": 1\n}\n")
	mustWriteFile(t, configPath, custom)
	if err := os.RemoveAll(filepath.Join(root, "logs")); err != nil {
		t.Fatal(err)
	}
	mustWriteFile(t, filepath.Join(root, "data", "preserve.txt"), []byte("keep"))

	result, err := initialize(op)
	if err != nil {
		t.Fatalf("second initialize() error = %v", err)
	}
	if !result.AlreadyInitialized {
		t.Fatal("second initialize did not report existing installation")
	}
	assertFileContent(t, configPath, string(custom))
	assertDirectory(t, filepath.Join(root, "logs"))
	assertFileContent(t, filepath.Join(root, "data", "preserve.txt"), "keep")
}

func TestInitializeRejectsRequiredDirectoryConflict(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))
	conflict := filepath.Join(root, "downloads")
	mustWriteFile(t, conflict, []byte("preserve"))

	_, err := initialize(operationsForExecutable(executable))
	if err == nil || !strings.Contains(err.Error(), "downloads") {
		t.Fatalf("initialize() error = %v, want conflict path", err)
	}
	assertFileContent(t, conflict, "preserve")
}

func TestInitializeReportsDirectoryCreationFailure(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))
	op := operationsForExecutable(executable)
	op.mkdirAll = func(path string, mode os.FileMode) error {
		return os.ErrPermission
	}

	_, err := initialize(op)
	if err == nil || !strings.Contains(err.Error(), "config") {
		t.Fatalf("initialize() error = %v, want unwritable config path", err)
	}
}

func TestInitializeRejectsInvalidOrUnsupportedConfiguration(t *testing.T) {
	t.Parallel()

	tests := []struct {
		name    string
		content string
		want    string
	}{
		{name: "invalid JSON", content: "{", want: "invalid JSON"},
		{name: "unsupported schema", content: "{\"schemaVersion\":2}\n", want: "unsupported schemaVersion 2"},
		{name: "unknown field", content: "{\"schemaVersion\":1,\"other\":true}\n", want: "invalid JSON"},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			t.Parallel()
			root := t.TempDir()
			executable := filepath.Join(root, executableName())
			configPath := filepath.Join(root, configDirectory, configFilename)
			mustWriteFile(t, executable, []byte("binary"))
			mustWriteFile(t, configPath, []byte(test.content))

			_, err := initialize(operationsForExecutable(executable))
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("initialize() error = %v, want %q", err, test.want)
			}
			assertFileContent(t, configPath, test.content)
		})
	}
}

func TestConfigurationPublicationFailureLeavesNoPartialDestination(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	executable := filepath.Join(root, executableName())
	mustWriteFile(t, executable, []byte("binary"))
	op := operationsForExecutable(executable)
	op.rename = func(oldPath, newPath string) error {
		return errors.New("injected publication failure")
	}

	_, err := initialize(op)
	if err == nil || !strings.Contains(err.Error(), "injected publication failure") {
		t.Fatalf("initialize() error = %v, want publication failure", err)
	}
	configPath := filepath.Join(root, configDirectory, configFilename)
	if _, err := os.Stat(configPath); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("configuration destination exists or stat failed unexpectedly: %v", err)
	}
	temporary, err := filepath.Glob(filepath.Join(root, configDirectory, ".coquena.json-*"))
	if err != nil || len(temporary) != 0 {
		t.Fatalf("temporary files = %v, error = %v; want none", temporary, err)
	}
}

func operationsForExecutable(executable string) operations {
	op := defaultOperations()
	op.executable = func() (string, error) { return executable, nil }
	return op
}

func executableName() string {
	if runtime.GOOS == "windows" {
		return "coquena.exe"
	}
	return "coquena"
}

func mustWriteFile(t *testing.T, path string, data []byte) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, data, 0o644); err != nil {
		t.Fatal(err)
	}
}

func assertDirectory(t *testing.T, path string) {
	t.Helper()
	info, err := os.Stat(path)
	if err != nil {
		t.Fatalf("stat directory %q: %v", path, err)
	}
	if !info.IsDir() {
		t.Fatalf("%q is not a directory", path)
	}
}

func assertFileContent(t *testing.T, path, want string) {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %q: %v", path, err)
	}
	if string(data) != want {
		t.Fatalf("%q content = %q, want %q", path, string(data), want)
	}
}
