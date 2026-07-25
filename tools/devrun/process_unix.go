//go:build !windows

package main

import (
	"errors"
	"os/exec"
	"sync"
	"syscall"
)

type processController struct {
	mu     sync.Mutex
	groups []int
}

func newProcessController() (*processController, error) { return &processController{}, nil }

func (*processController) configure(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true}
}

func (controller *processController) started(cmd *exec.Cmd) error {
	controller.mu.Lock()
	defer controller.mu.Unlock()
	controller.groups = append(controller.groups, cmd.Process.Pid)
	return nil
}

func (controller *processController) beginShutdown() { controller.signal(syscall.SIGTERM) }

func (controller *processController) forceExit(int) error { return controller.signal(syscall.SIGKILL) }

func (controller *processController) signal(signal syscall.Signal) error {
	controller.mu.Lock()
	defer controller.mu.Unlock()
	var result error
	for _, group := range controller.groups {
		if err := syscall.Kill(-group, signal); err != nil && !errors.Is(err, syscall.ESRCH) {
			result = errors.Join(result, err)
		}
	}
	return result
}

func (*processController) close() error { return nil }
