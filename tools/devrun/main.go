package main

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"os/signal"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"time"

	"github.com/mstranich/coquena/internal/installation"
)

const shutdownGrace = 2500 * time.Millisecond

type childResult struct {
	name string
	err  error
}

func main() { os.Exit(run()) }

func run() int {
	repositoryRoot, err := filepath.Abs(".")
	if err != nil {
		return fail("resolve repository root", err)
	}
	home := filepath.Join(repositoryRoot, "home")
	if err := os.Setenv("COQUENA_HOME", home); err != nil {
		return fail("set COQUENA_HOME", err)
	}
	if _, err := installation.Initialize(); err != nil {
		return fail("initialize development home", err)
	}

	commands, err := developmentCommands(repositoryRoot)
	if err != nil {
		return fail("prepare development commands", err)
	}
	controller, err := newProcessController()
	if err != nil {
		return fail("establish process-tree ownership", err)
	}
	defer controller.close()
	results := make(chan childResult, len(commands))
	started := make([]*exec.Cmd, 0, len(commands))
	for _, named := range commands {
		cmd := named.cmd
		cmd.Dir = repositoryRoot
		cmd.Env = withEnvironment(os.Environ(), "COQUENA_HOME", home)
		cmd.Stdin, cmd.Stdout, cmd.Stderr = os.Stdin, os.Stdout, os.Stderr
		controller.configure(cmd)
		if err := cmd.Start(); err != nil {
			fmt.Fprintf(os.Stderr, "devrun: start %s: %v\n", named.name, err)
			controller.beginShutdown()
			_ = controller.forceExit(1)
			return 1
		}
		if err := controller.started(cmd); err != nil {
			fmt.Fprintf(os.Stderr, "devrun: register %s: %v\n", named.name, err)
			controller.beginShutdown()
			_ = controller.forceExit(1)
			return 1
		}
		started = append(started, cmd)
		go func(name string, process *exec.Cmd) { results <- childResult{name, process.Wait()} }(named.name, cmd)
	}

	fmt.Printf("[devrun] pids: devrun=%d air=%d web=%d\n", os.Getpid(), started[0].Process.Pid, started[1].Process.Pid)

	signals := make(chan os.Signal, 1)
	signal.Notify(signals, os.Interrupt, syscall.SIGTERM)
	defer signal.Stop(signals)

	exitCode := 0
	remaining := len(started)
	select {
	case <-signals:
		exitCode = 130
	case result := <-results:
		remaining--
		exitCode = processExitCode(result.err)
		if result.err != nil {
			fmt.Fprintf(os.Stderr, "devrun: %s exited: %v\n", result.name, result.err)
		}
	}

	controller.beginShutdown()
	timer := time.NewTimer(shutdownGrace)
	defer timer.Stop()
	for remaining > 0 {
		select {
		case <-results:
			remaining--
		case <-timer.C:
			remaining = 0
		}
	}
	if err := controller.forceExit(exitCode); err != nil {
		fmt.Fprintf(os.Stderr, "devrun: force supervised shutdown: %v\n", err)
		if closeErr := controller.close(); closeErr != nil {
			fmt.Fprintf(os.Stderr, "devrun: close process ownership handle: %v\n", closeErr)
		}
		return 1
	}
	return exitCode
}

type namedCommand struct {
	name string
	cmd  *exec.Cmd
}

func developmentCommands(repositoryRoot string) ([]namedCommand, error) {
	air, err := exec.LookPath("air")
	if err != nil {
		return nil, fmt.Errorf("find air: %w", err)
	}
	web, err := npmCommand(repositoryRoot)
	if err != nil {
		return nil, err
	}
	return []namedCommand{
		{name: "air", cmd: exec.Command(air, "-c", ".air.toml")},
		{name: "web", cmd: web},
	}, nil
}

func npmCommand(repositoryRoot string) (*exec.Cmd, error) {
	args := []string{"--prefix", "apps/web", "run", "start"}
	if runtime.GOOS != "windows" {
		npm, err := exec.LookPath("npm")
		if err != nil {
			return nil, fmt.Errorf("find npm: %w", err)
		}
		return exec.Command(npm, args...), nil
	}
	node, err := exec.LookPath("node.exe")
	if err != nil {
		return nil, fmt.Errorf("find node.exe: %w", err)
	}
	npmCLI := filepath.Join(filepath.Dir(node), "node_modules", "npm", "bin", "npm-cli.js")
	if _, err := os.Stat(npmCLI); err != nil {
		return nil, fmt.Errorf("find npm CLI beside node.exe: %w", err)
	}
	return exec.Command(node, append([]string{npmCLI}, args...)...), nil
}

func withEnvironment(environment []string, key, value string) []string {
	prefix := strings.ToUpper(key) + "="
	result := make([]string, 0, len(environment)+1)
	for _, entry := range environment {
		if !strings.HasPrefix(strings.ToUpper(entry), prefix) {
			result = append(result, entry)
		}
	}
	return append(result, key+"="+value)
}

func processExitCode(err error) int {
	if err == nil {
		return 0
	}
	var exitError *exec.ExitError
	if errors.As(err, &exitError) {
		return exitError.ExitCode()
	}
	return 1
}

func fail(action string, err error) int {
	fmt.Fprintf(os.Stderr, "devrun: %s: %v\n", action, err)
	return 1
}
