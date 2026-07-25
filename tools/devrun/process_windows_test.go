//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"golang.org/x/sys/windows"
)

func TestJobObjectKillsChildWhenSupervisorDies(t *testing.T) {
	mode := os.Getenv("DEVRUN_JOB_TEST_MODE")
	switch mode {
	case "child":
		for {
			time.Sleep(time.Hour)
		}
	case "supervisor":
		controller, err := newProcessController()
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
		defer controller.close()
		child := exec.Command(os.Args[0], "-test.run=TestJobObjectKillsChildWhenSupervisorDies")
		child.Env = append(os.Environ(), "DEVRUN_JOB_TEST_MODE=child")
		controller.configure(child)
		if err := child.Start(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(3)
		}
		if err := controller.started(child); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(4)
		}
		if err := os.WriteFile(os.Getenv("DEVRUN_JOB_TEST_PID_FILE"), []byte(strconv.Itoa(child.Process.Pid)), 0o600); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(5)
		}
		for {
			time.Sleep(time.Hour)
		}
	}

	pidFile := filepath.Join(t.TempDir(), "child.pid")
	supervisor := exec.Command(os.Args[0], "-test.run=TestJobObjectKillsChildWhenSupervisorDies")
	supervisor.Env = append(os.Environ(), "DEVRUN_JOB_TEST_MODE=supervisor", "DEVRUN_JOB_TEST_PID_FILE="+pidFile)
	var diagnostics strings.Builder
	supervisor.Stderr = &diagnostics
	if err := supervisor.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { _ = supervisor.Process.Kill() }()

	var childPID int
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		data, err := os.ReadFile(pidFile)
		if err == nil {
			childPID, err = strconv.Atoi(string(data))
			if err != nil {
				t.Fatal(err)
			}
			break
		}
		time.Sleep(25 * time.Millisecond)
	}
	if childPID == 0 {
		t.Fatalf("supervisor did not start child: %s", diagnostics.String())
	}
	if err := supervisor.Process.Kill(); err != nil {
		t.Fatal(err)
	}
	_ = supervisor.Wait()

	deadline = time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		handle, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION, false, uint32(childPID))
		if err != nil {
			return
		}
		_ = windows.CloseHandle(handle)
		time.Sleep(25 * time.Millisecond)
	}
	t.Fatalf("child PID %d survived forced supervisor termination", childPID)
}
