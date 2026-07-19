package main

import (
	"bytes"
	"testing"
)

func TestRunPrintsDevelopmentIdentity(t *testing.T) {
	t.Parallel()

	var output bytes.Buffer

	if err := run(&output); err != nil {
		t.Fatalf("run() error = %v", err)
	}

	const want = "Coquena development\n"
	if got := output.String(); got != want {
		t.Fatalf("run() output = %q, want %q", got, want)
	}
}
