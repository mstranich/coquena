package main

import (
	"path/filepath"
	"strings"
	"testing"
)

func TestWithEnvironmentReplacesExistingValueCaseInsensitively(t *testing.T) {
	result := withEnvironment([]string{"PATH=one", "coquena_home=old"}, "COQUENA_HOME", filepath.Join("root", "home"))
	var matches []string
	for _, entry := range result {
		if strings.HasPrefix(strings.ToUpper(entry), "COQUENA_HOME=") {
			matches = append(matches, entry)
		}
	}
	if len(matches) != 1 || matches[0] != "COQUENA_HOME="+filepath.Join("root", "home") {
		t.Fatalf("unexpected environment entries: %v", result)
	}
}

func TestDevelopmentCommandsUseAirAndWebStart(t *testing.T) {
	commands, err := developmentCommands(filepath.Clean("../.."))
	if err != nil {
		t.Skipf("development tools unavailable: %v", err)
	}
	if len(commands) != 2 || commands[0].name != "air" || commands[1].name != "web" {
		t.Fatalf("unexpected commands: %#v", commands)
	}
	if got := commands[0].cmd.Args[1:]; strings.Join(got, " ") != "-c .air.toml" {
		t.Fatalf("unexpected Air args: %v", got)
	}
	webArgs := strings.Join(commands[1].cmd.Args, " ")
	if !strings.Contains(webArgs, "--prefix apps/web run start") {
		t.Fatalf("unexpected web command: %s", webArgs)
	}
}
