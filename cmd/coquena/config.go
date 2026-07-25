package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
)

const configUsage = "usage: coquena config models-directory [<absolute-path>] [--json]"

func runConfig(args []string, out io.Writer, get modelsDirectoryFunc, set setModelsDirectoryFunc) error {
	jsonOutput, args, err := extractJSON(args)
	if err != nil || len(args) < 1 || len(args) > 2 || args[0] != "models-directory" {
		return errors.New(configUsage)
	}
	var path string
	if len(args) == 1 {
		path, err = get()
	} else {
		path, err = set(args[1])
	}
	if err != nil {
		return err
	}
	if jsonOutput {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int `json:"schemaVersion"`
			Result        struct {
				Path string `json:"path"`
			} `json:"result"`
		}{SchemaVersion: 1, Result: struct {
			Path string `json:"path"`
		}{path}})
	}
	_, err = fmt.Fprintln(out, path)
	return err
}
