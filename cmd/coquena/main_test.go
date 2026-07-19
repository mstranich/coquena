package main

import (
	"bytes"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/installation"
)

func TestExecuteInitReportsNewInstallation(t *testing.T) {
	t.Parallel()

	root := filepath.Join("portable", "coquena")
	var stdout, stderr bytes.Buffer
	code := execute([]string{"init"}, &stdout, &stderr, func() (installation.Result, error) {
		return installation.Result{Root: root}, nil
	})

	if code != 0 {
		t.Fatalf("execute() code = %d, want 0; stderr = %q", code, stderr.String())
	}
	if want := "Initialized Coquena in " + root + "\n"; stdout.String() != want {
		t.Fatalf("stdout = %q, want %q", stdout.String(), want)
	}
	if stderr.Len() != 0 {
		t.Fatalf("stderr = %q, want empty", stderr.String())
	}
}

func TestExecuteInitReportsExistingInstallation(t *testing.T) {
	t.Parallel()

	var stdout, stderr bytes.Buffer
	code := execute([]string{"init"}, &stdout, &stderr, func() (installation.Result, error) {
		return installation.Result{Root: "existing", AlreadyInitialized: true}, nil
	})

	if code != 0 || stdout.String() != "Coquena is already initialized in existing\n" || stderr.Len() != 0 {
		t.Fatalf("execute() = (%d, %q, %q), want successful existing result", code, stdout.String(), stderr.String())
	}
}

func TestExecuteRejectsUnsupportedArguments(t *testing.T) {
	t.Parallel()

	var stdout, stderr bytes.Buffer
	code := execute([]string{"serve"}, &stdout, &stderr, func() (installation.Result, error) {
		t.Fatal("initialize called for unsupported command")
		return installation.Result{}, nil
	})

	if code != 1 || stdout.Len() != 0 || !strings.Contains(stderr.String(), "usage: coquena init") {
		t.Fatalf("execute() = (%d, %q, %q), want usage failure", code, stdout.String(), stderr.String())
	}
}

func TestExecuteReportsInitializationFailure(t *testing.T) {
	t.Parallel()

	var stdout, stderr bytes.Buffer
	code := execute([]string{"init"}, &stdout, &stderr, func() (installation.Result, error) {
		return installation.Result{}, errors.New("D:/read-only: access denied")
	})

	if code != 1 || stdout.Len() != 0 || !strings.Contains(stderr.String(), "D:/read-only: access denied") {
		t.Fatalf("execute() = (%d, %q, %q), want path-based failure", code, stdout.String(), stderr.String())
	}
}

func TestCoquenaInitInitializesBesideBinaryFromAnotherWorkingDirectory(t *testing.T) {
	if testing.Short() {
		t.Skip("skipping executable integration test in short mode")
	}

	installationRoot := t.TempDir()
	workingDirectory := t.TempDir()
	binaryName := "coquena"
	if runtime.GOOS == "windows" {
		binaryName += ".exe"
	}
	binaryPath := filepath.Join(installationRoot, binaryName)

	build := exec.Command("go", "build", "-o", binaryPath, ".")
	if output, err := build.CombinedOutput(); err != nil {
		t.Fatalf("build Coquena: %v\n%s", err, output)
	}

	command := exec.Command(binaryPath, "init")
	command.Dir = workingDirectory
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("coquena init: %v\n%s", err, output)
	}
	if !strings.Contains(string(output), installationRoot) {
		t.Fatalf("coquena init output = %q, want installation root %q", output, installationRoot)
	}

	for _, directory := range []string{"config", "data", "downloads", "logs", "runtimes", "tmp"} {
		info, err := os.Stat(filepath.Join(installationRoot, directory))
		if err != nil || !info.IsDir() {
			t.Fatalf("portable directory %q: info = %v, error = %v", directory, info, err)
		}
		if _, err := os.Stat(filepath.Join(workingDirectory, directory)); !errors.Is(err, os.ErrNotExist) {
			t.Fatalf("working directory contains %q or stat failed unexpectedly: %v", directory, err)
		}
	}
}
