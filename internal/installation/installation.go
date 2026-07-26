package installation

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

const (
	configDirectory = "config"
	configFilename  = "coquena.json"
	schemaVersion   = 3
)

var requiredDirectories = []string{
	configDirectory,
	"data",
	"logs",
	"models",
	"runtimes",
	"tmp",
}

// Result describes the portable installation found or created by Initialize.
type Result struct {
	Root               string
	AlreadyInitialized bool
}

type configuration struct {
	SchemaVersion int                    `json:"schemaVersion"`
	Models        *modelsConfiguration   `json:"models,omitempty"`
	Runtimes      *runtimesConfiguration `json:"runtimes,omitempty"`
}

type modelsConfiguration struct {
	Directory string `json:"directory"`
}
type runtimesConfiguration struct {
	Directory string `json:"directory"`
}

type operations struct {
	executable func() (string, error)
	lookupEnv  func(string) (string, bool)
	evalLinks  func(string) (string, error)
	abs        func(string) (string, error)
	stat       func(string) (os.FileInfo, error)
	mkdirAll   func(string, os.FileMode) error
	readFile   func(string) ([]byte, error)
	createTemp func(string, string) (*os.File, error)
	rename     func(string, string) error
	remove     func(string) error
}

func defaultOperations() operations {
	return operations{
		executable: os.Executable,
		lookupEnv:  os.LookupEnv,
		evalLinks:  filepath.EvalSymlinks,
		abs:        filepath.Abs,
		stat:       os.Stat,
		mkdirAll:   os.MkdirAll,
		readFile:   os.ReadFile,
		createTemp: os.CreateTemp,
		rename:     os.Rename,
		remove:     os.Remove,
	}
}

// Initialize creates or validates the selected portable installation.
func Initialize() (Result, error) {
	return initialize(defaultOperations())
}

// Home returns the selected application home. COQUENA_HOME takes precedence
// when explicitly set; otherwise the resolved executable directory is used.
func Home() (string, error) { return resolveHome(defaultOperations()) }

// ModelsDirectory returns the configured directory or the portable default.
func ModelsDirectory() (string, error) { return modelsDirectory(defaultOperations()) }

// SetModelsDirectory validates and atomically records an explicit directory.
func SetModelsDirectory(path string) (string, error) {
	return setModelsDirectory(defaultOperations(), path)
}
func RuntimesDirectory() (string, error) { return runtimesDirectory(defaultOperations()) }
func SetRuntimesDirectory(path string) (string, error) {
	return setRuntimesDirectory(defaultOperations(), path)
}

func initialize(op operations) (Result, error) {
	root, err := resolveHome(op)
	if err != nil {
		return Result{}, err
	}

	result := Result{Root: root}
	configPath := filepath.Join(root, configDirectory, configFilename)
	existingConfig, err := readExistingConfiguration(op, configPath)
	if err != nil {
		return Result{}, err
	}
	result.AlreadyInitialized = existingConfig

	if err := createDirectories(op, root); err != nil {
		return Result{}, err
	}

	if !existingConfig {
		if err := publishConfiguration(op, configPath); err != nil {
			return Result{}, err
		}
	}

	return result, nil
}

func resolveRoot(op operations) (string, error) {
	executablePath, err := op.executable()
	if err != nil {
		return "", fmt.Errorf("resolve executable path: %w", err)
	}
	resolvedPath, err := op.evalLinks(executablePath)
	if err != nil {
		return "", fmt.Errorf("resolve executable link %q: %w", executablePath, err)
	}
	absolutePath, err := op.abs(resolvedPath)
	if err != nil {
		return "", fmt.Errorf("make executable path %q absolute: %w", resolvedPath, err)
	}

	return filepath.Dir(filepath.Clean(absolutePath)), nil
}

func resolveHome(op operations) (string, error) {
	if value, present := op.lookupEnv("COQUENA_HOME"); present {
		if value == "" || !filepath.IsAbs(value) {
			return "", errors.New("COQUENA_HOME must be an absolute non-empty path")
		}
		absolute, err := op.abs(value)
		if err != nil {
			return "", fmt.Errorf("resolve COQUENA_HOME %q: %w", value, err)
		}
		return filepath.Clean(absolute), nil
	}
	return resolveRoot(op)
}

func createDirectories(op operations, root string) error {
	for _, name := range requiredDirectories {
		path := filepath.Join(root, name)
		info, err := op.stat(path)
		switch {
		case err == nil && !info.IsDir():
			return fmt.Errorf("required directory path %q is not a directory", path)
		case err == nil:
			continue
		case !errors.Is(err, os.ErrNotExist):
			return fmt.Errorf("inspect required directory %q: %w", path, err)
		}

		if err := op.mkdirAll(path, 0o755); err != nil {
			return fmt.Errorf("create required directory %q: %w", path, err)
		}
	}

	return nil
}

func readConfiguration(op operations, path string) (configuration, bool, error) {
	data, err := op.readFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return configuration{}, false, nil
	}
	if err != nil {
		return configuration{}, false, fmt.Errorf("read configuration %q: %w", path, err)
	}
	c, err := parseConfiguration(data)
	if err != nil {
		return configuration{}, false, fmt.Errorf("validate existing configuration %q: %w", path, err)
	}
	return c, true, nil
}

func readExistingConfiguration(op operations, path string) (bool, error) {
	_, ok, err := readConfiguration(op, path)
	return ok, err
}

func initialConfiguration() ([]byte, error) {
	data, err := json.MarshalIndent(configuration{SchemaVersion: schemaVersion}, "", "  ")
	if err != nil {
		return nil, fmt.Errorf("encode initial configuration: %w", err)
	}
	data = append(data, '\n')
	if err := validateConfiguration(data); err != nil {
		return nil, fmt.Errorf("validate initial configuration: %w", err)
	}
	return data, nil
}

func parseConfiguration(data []byte) (configuration, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()

	var config configuration
	if err := decoder.Decode(&config); err != nil {
		return configuration{}, fmt.Errorf("invalid JSON: %w", err)
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		return configuration{}, errors.New("invalid JSON: multiple values")
	}
	if config.SchemaVersion != schemaVersion {
		return configuration{}, fmt.Errorf("unsupported schemaVersion %d", config.SchemaVersion)
	}
	return config, nil
}

func validateConfiguration(data []byte) error { _, err := parseConfiguration(data); return err }

func modelsDirectory(op operations) (string, error) {
	root, err := resolveHome(op)
	if err != nil {
		return "", err
	}
	c, ok, err := readConfiguration(op, filepath.Join(root, configDirectory, configFilename))
	if err != nil {
		return "", err
	}
	if !ok {
		return "", errors.New("portable installation is not initialized; run coquena init")
	}
	if c.Models != nil && c.Models.Directory != "" {
		return c.Models.Directory, nil
	}
	return filepath.Join(root, "models"), nil
}

func setModelsDirectory(op operations, value string) (string, error) {
	if value == "" || !filepath.IsAbs(value) {
		return "", errors.New("models directory must be an absolute non-empty path")
	}
	path, err := op.abs(value)
	if err != nil {
		return "", err
	}
	path = filepath.Clean(path)
	info, err := op.stat(path)
	if err != nil {
		return "", fmt.Errorf("inspect models directory %q: %w", path, err)
	}
	if !info.IsDir() {
		return "", fmt.Errorf("models directory %q is not a directory", path)
	}
	tmp, err := op.createTemp(path, ".coquena-write-probe-*")
	if err != nil {
		return "", fmt.Errorf("models directory %q is not writable: %w", path, err)
	}
	tmpName := tmp.Name()
	_ = tmp.Close()
	_ = op.remove(tmpName)
	root, err := resolveHome(op)
	if err != nil {
		return "", err
	}
	configPath := filepath.Join(root, configDirectory, configFilename)
	_, ok, err := readConfiguration(op, configPath)
	if err != nil {
		return "", err
	}
	if !ok {
		return "", errors.New("portable installation is not initialized; run coquena init")
	}
	c, _, err := readConfiguration(op, configPath)
	if err != nil {
		return "", err
	}
	c.SchemaVersion = schemaVersion
	c.Models = &modelsConfiguration{Directory: path}
	b, err := json.MarshalIndent(c, "", "  ")
	if err != nil {
		return "", err
	}
	b = append(b, '\n')
	if err := publishConfigurationBytes(op, configPath, b); err != nil {
		return "", err
	}
	return path, nil
}

func runtimesDirectory(op operations) (string, error) {
	root, err := resolveHome(op)
	if err != nil {
		return "", err
	}
	c, ok, err := readConfiguration(op, filepath.Join(root, configDirectory, configFilename))
	if err != nil {
		return "", err
	}
	if !ok {
		return "", errors.New("portable installation is not initialized; run coquena init")
	}
	if c.Runtimes != nil && c.Runtimes.Directory != "" {
		return c.Runtimes.Directory, nil
	}
	return filepath.Join(root, "runtimes"), nil
}

func setRuntimesDirectory(op operations, value string) (string, error) {
	path, err := validateDirectory(op, value, "runtimes")
	if err != nil {
		return "", err
	}
	root, err := resolveHome(op)
	if err != nil {
		return "", err
	}
	configPath := filepath.Join(root, configDirectory, configFilename)
	c, ok, err := readConfiguration(op, configPath)
	if err != nil {
		return "", err
	}
	if !ok {
		return "", errors.New("portable installation is not initialized; run coquena init")
	}
	c.SchemaVersion = schemaVersion
	c.Runtimes = &runtimesConfiguration{Directory: path}
	b, err := json.MarshalIndent(c, "", "  ")
	if err != nil {
		return "", err
	}
	b = append(b, '\n')
	if err := publishConfigurationBytes(op, configPath, b); err != nil {
		return "", err
	}
	return path, nil
}

func validateDirectory(op operations, value, label string) (string, error) {
	if value == "" || !filepath.IsAbs(value) {
		return "", fmt.Errorf("%s directory must be an absolute non-empty path", label)
	}
	path, err := op.abs(value)
	if err != nil {
		return "", err
	}
	path = filepath.Clean(path)
	info, err := op.stat(path)
	if err != nil {
		return "", fmt.Errorf("inspect %s directory %q: %w", label, path, err)
	}
	if !info.IsDir() {
		return "", fmt.Errorf("%s directory %q is not a directory", label, path)
	}
	tmp, err := op.createTemp(path, ".coquena-write-probe-*")
	if err != nil {
		return "", fmt.Errorf("%s directory %q is not writable: %w", label, path, err)
	}
	tmpName := tmp.Name()
	_ = tmp.Close()
	_ = op.remove(tmpName)
	return path, nil
}

func publishConfiguration(op operations, path string) (err error) {
	data, err := initialConfiguration()
	if err != nil {
		return err
	}

	return publishConfigurationBytes(op, path, data)
}

func publishConfigurationBytes(op operations, path string, data []byte) (err error) {
	temporary, err := op.createTemp(filepath.Dir(path), ".coquena.json-*")
	if err != nil {
		return fmt.Errorf("create temporary configuration beside %q: %w", path, err)
	}
	temporaryPath := temporary.Name()
	defer func() {
		_ = temporary.Close()
		_ = op.remove(temporaryPath)
	}()

	if _, err = temporary.Write(data); err != nil {
		return fmt.Errorf("write temporary configuration %q: %w", temporaryPath, err)
	}
	if err = temporary.Sync(); err != nil {
		return fmt.Errorf("sync temporary configuration %q: %w", temporaryPath, err)
	}
	if err = temporary.Close(); err != nil {
		return fmt.Errorf("close temporary configuration %q: %w", temporaryPath, err)
	}
	if _, err = op.stat(path); err != nil && !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("inspect configuration destination %q: %w", path, err)
	}
	if err = op.rename(temporaryPath, path); err != nil {
		return fmt.Errorf("publish configuration %q: %w", path, err)
	}

	return nil
}
