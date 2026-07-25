//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"sync"
	"unsafe"

	"golang.org/x/sys/windows"
)

type processController struct {
	job       windows.Handle
	closeOnce sync.Once
	closeErr  error
}

func newProcessController() (*processController, error) {
	job, err := windows.CreateJobObject(nil, nil)
	if err != nil {
		return nil, fmt.Errorf("create Windows Job Object: %w", err)
	}
	controller := &processController{job: job}
	info := windows.JOBOBJECT_EXTENDED_LIMIT_INFORMATION{}
	info.BasicLimitInformation.LimitFlags = windows.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
	if _, err := windows.SetInformationJobObject(job, windows.JobObjectExtendedLimitInformation, uintptr(unsafe.Pointer(&info)), uint32(unsafe.Sizeof(info))); err != nil {
		_ = controller.close()
		return nil, fmt.Errorf("enable JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: %w", err)
	}
	process, err := windows.OpenProcess(windows.PROCESS_SET_QUOTA|windows.PROCESS_TERMINATE, false, uint32(os.Getpid()))
	if err != nil {
		_ = controller.close()
		return nil, fmt.Errorf("open devrun process for Job Object assignment: %w", err)
	}
	defer windows.CloseHandle(process)
	if err := windows.AssignProcessToJobObject(job, process); err != nil {
		_ = controller.close()
		return nil, fmt.Errorf("could not assign devrun to its Windows Job Object: %w; the parent process, terminal, CI, or test harness may impose a restrictive Job Object; devrun will not start without process-tree ownership", err)
	}
	return controller, nil
}

func (*processController) configure(*exec.Cmd)     {}
func (*processController) started(*exec.Cmd) error { return nil }
func (*processController) beginShutdown()          {}

func (controller *processController) forceExit(exitCode int) error {
	return windows.TerminateJobObject(controller.job, uint32(exitCode))
}

func (controller *processController) close() error {
	controller.closeOnce.Do(func() { controller.closeErr = windows.CloseHandle(controller.job) })
	return controller.closeErr
}
