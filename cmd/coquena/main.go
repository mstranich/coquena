package main

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/signal"
	"syscall"

	"github.com/mstranich/coquena/internal/installation"
	hostsystem "github.com/mstranich/coquena/internal/system"
	"github.com/mstranich/coquena/internal/webui"
)

type initializeFunc func() (installation.Result, error)
type inspectFunc func() (hostsystem.Report, error)

func main() {
	os.Exit(executeWithRuntime(os.Args[1:], os.Stdout, os.Stderr, installation.Initialize, hostsystem.Inspect, newRuntimeManager()))
}

func execute(args []string, stdout, stderr io.Writer, initialize initializeFunc, inspectors ...inspectFunc) int {
	inspect := hostsystem.Inspect
	if len(inspectors) > 0 {
		inspect = inspectors[0]
	}
	if err := run(args, stdout, initialize, inspect, nil, installation.ModelsDirectory, installation.SetModelsDirectory); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}

	return 0
}

func executeWithRuntime(args []string, stdout, stderr io.Writer, initialize initializeFunc, inspect inspectFunc, runtimes runtimeService) int {
	if err := run(args, stdout, initialize, inspect, runtimes, installation.ModelsDirectory, installation.SetModelsDirectory); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	return 0
}

type modelsDirectoryFunc func() (string, error)
type setModelsDirectoryFunc func(string) (string, error)

func run(args []string, stdout io.Writer, initialize initializeFunc, inspect inspectFunc, runtimes runtimeService, models modelsDirectoryFunc, setModels setModelsDirectoryFunc) error {
	if len(args) == 1 && args[0] == "init" {
		return runInit(stdout, initialize)
	}
	if len(args) > 0 && args[0] == "webui" {
		return runWebUI(args[1:], stdout, os.Stderr, runtimes)
	}
	if len(args) == 1 && args[0] == "system" {
		return runSystem(stdout, inspect, false)
	}
	if len(args) == 2 && args[0] == "system" && args[1] == "--json" {
		return runSystem(stdout, inspect, true)
	}
	if len(args) > 0 && args[0] == "runtime" {
		return runRuntime(args[1:], stdout, runtimes)
	}
	if len(args) > 0 && args[0] == "model" {
		return runModel(args[1:], stdout)
	}
	if len(args) > 0 && args[0] == "field" {
		return runField(args[1:], stdout)
	}
	if len(args) > 0 && args[0] == "download" {
		return runDownload(args[1:], stdout)
	}
	if len(args) > 0 && args[0] == "config" {
		return runConfig(args[1:], stdout, models, setModels, modelsDirectoryFunc(installation.RuntimesDirectory), setModelsDirectoryFunc(installation.SetRuntimesDirectory))
	}
	return errors.New(commandUsage)
}

func runWebUI(args []string, stdout, stderr io.Writer, runtimes runtimeService) error {
	port, err := webui.ParsePort(args)
	if err != nil {
		return err
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if runtimes == nil {
		return webui.Serve(ctx, port, stdout, stderr)
	}
	if provider, ok := runtimes.(interface{ sharedRuntimeService() webui.RuntimeService }); ok {
		return webui.Serve(ctx, port, stdout, stderr, provider.sharedRuntimeService())
	}
	return webui.Serve(ctx, port, stdout, stderr)
}

func runInit(stdout io.Writer, initialize initializeFunc) error {
	result, err := initialize()
	if err != nil {
		return fmt.Errorf("initialize Coquena: %w", err)
	}

	if result.AlreadyInitialized {
		_, err = fmt.Fprintf(stdout, "Coquena is already initialized in %s\n", result.Root)
	} else {
		_, err = fmt.Fprintf(stdout, "Initialized Coquena in %s\n", result.Root)
	}
	return err
}

func runSystem(stdout io.Writer, inspect inspectFunc, jsonOutput bool) error {
	report, err := inspect()
	if err != nil {
		return fmt.Errorf("inspect system: %w", err)
	}
	if jsonOutput {
		if err := hostsystem.WriteJSON(stdout, report); err != nil {
			return fmt.Errorf("write system JSON: %w", err)
		}
		return nil
	}
	if err := hostsystem.WriteText(stdout, report); err != nil {
		return fmt.Errorf("write system report: %w", err)
	}
	return nil
}
