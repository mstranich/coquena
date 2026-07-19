package main

import (
	"fmt"
	"io"
	"os"
)

func main() {
	if err := run(os.Stdout); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(output io.Writer) error {
	_, err := fmt.Fprintln(output, "Coquena development")
	return err
}
