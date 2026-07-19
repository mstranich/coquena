package main

import (
	"errors"
	"fmt"
	"io"
	"os"

	"github.com/mstranich/coquena/internal/installation"
)

type initializeFunc func() (installation.Result, error)

func main() {
	os.Exit(execute(os.Args[1:], os.Stdout, os.Stderr, installation.Initialize))
}

func execute(args []string, stdout, stderr io.Writer, initialize initializeFunc) int {
	if err := run(args, stdout, initialize); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}

	return 0
}

func run(args []string, stdout io.Writer, initialize initializeFunc) error {
	if len(args) != 1 || args[0] != "init" {
		return errors.New("usage: coquena init")
	}

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
