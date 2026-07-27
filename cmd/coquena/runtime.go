package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strconv"
	"strings"

	runtimeapp "github.com/mstranich/coquena/internal/runtime"
)

const commandUsage = `usage: coquena init
       coquena webui [--port <port>]
       coquena system [--json]
       coquena runtime remote [latest|<tag>] [<backend-selector>] [--json]
       coquena runtime remote --history <count> [<backend-selector>] [--json]
       coquena runtime download <asset-id|backend-selector> [<backend-selector>] [--json]
       coquena runtime download list|status [<job-id>] [--json]
       coquena runtime list [<backend-selector>] [--json]
       coquena runtime remove <asset-id> [--json]
	   coquena model resolve|inspect|download|revision <reference> [--json]
       coquena download list|status|pause|resume|cancel <job-id> [--json]
       coquena field define <kind> <name> [flags] [--json]
       coquena field remove|list|show <name> [--json]`

type runtimeQuery = runtimeapp.Query
type runtimeDownloadRequest = runtimeapp.DownloadRequest
type runtimeRecord = runtimeapp.Record
type runtimeMutation = runtimeapp.Mutation

// runtimeService is the adapter boundary between the CLI and runtime management.
// Implementations own compatibility filtering, exact asset recognition, shorthand
// resolution, ambiguity detection, and all filesystem/network effects.
type runtimeService interface {
	Remote(runtimeQuery) ([]runtimeRecord, error)
	Download(runtimeDownloadRequest) (runtimeMutation, error)
	List(selector string) ([]runtimeRecord, error)
	Remove(assetID string) (runtimeMutation, error)
}

func runRuntime(args []string, stdout io.Writer, service runtimeService) error {
	if service == nil {
		return errors.New("runtime management is unavailable")
	}
	jsonOutput, args, err := extractJSON(args)
	if err != nil || len(args) == 0 {
		return errors.New(commandUsage)
	}
	var result any
	defaultRemoteListing := args[0] == "remote" && len(args) == 1
	switch args[0] {
	case "remote":
		query, parseErr := parseRuntimeQuery(args[1:])
		if parseErr != nil {
			return parseErr
		}
		result, err = service.Remote(query)
	case "download":
		if len(args) >= 2 && isDownloadAction(args[1]) {
			nested := append([]string(nil), args[1:]...)
			if jsonOutput {
				nested = append(nested, "--json")
			}
			return runDownloadWithKind(nested, stdout, "runtime")
		}
		request, parseErr := parseRuntimeDownload(args[1:])
		if parseErr != nil {
			return parseErr
		}
		result, err = service.Download(request)
	case "list":
		if len(args) > 2 {
			return errors.New(commandUsage)
		}
		selector := ""
		if len(args) == 2 {
			selector = args[1]
		}
		result, err = service.List(selector)
	case "remove":
		if len(args) != 2 || args[1] == "" {
			return errors.New(commandUsage)
		}
		result, err = service.Remove(args[1])
	default:
		return errors.New(commandUsage)
	}
	if err != nil {
		var paused runtimeDownloadPaused
		if errors.As(err, &paused) {
			if !jsonOutput {
				if paused.assetID != "" {
					_, _ = fmt.Fprintf(stdout, "Download %s paused after interrupt. Run `coquena runtime download status %s` to inspect or `coquena runtime download resume %s` to continue.\n", paused.assetID, paused.assetID, paused.assetID)
				} else {
					_, _ = fmt.Fprintln(stdout, "Download paused after interrupt.")
				}
			}
			return nil
		}
		return err
	}
	if jsonOutput {
		document := struct {
			SchemaVersion int `json:"schemaVersion"`
			Result        any `json:"result"`
		}{SchemaVersion: 1, Result: result}
		if err := json.NewEncoder(stdout).Encode(document); err != nil {
			return fmt.Errorf("write runtime JSON: %w", err)
		}
		return nil
	}
	if err := writeRuntimeText(stdout, args[0], result); err != nil {
		return err
	}
	if defaultRemoteListing {
		if _, err := fmt.Fprintln(stdout, "\nShowing only the latest compatible release. Use --history <count> to include previous releases."); err != nil {
			return fmt.Errorf("write runtime hint: %w", err)
		}
	}
	return nil
}

func extractJSON(args []string) (bool, []string, error) {
	out := make([]string, 0, len(args))
	jsonOutput := false
	for _, arg := range args {
		if arg == "--json" {
			if jsonOutput {
				return false, nil, errors.New(commandUsage)
			}
			jsonOutput = true
		} else {
			out = append(out, arg)
		}
	}
	return jsonOutput, out, nil
}

func parseRuntimeQuery(args []string) (runtimeQuery, error) {
	q := runtimeQuery{Release: "latest"}
	for i := 0; i < len(args); i++ {
		if args[i] != "--history" {
			continue
		}
		if q.History != 0 || i+1 >= len(args) {
			return q, errors.New(commandUsage)
		}
		count, err := strconv.Atoi(args[i+1])
		if err != nil || count < 1 || count > 100 {
			return q, errors.New("runtime history count must be between 1 and 100")
		}
		q.History = count
		args = append(args[:i], args[i+2:]...)
		i--
	}
	for _, arg := range args {
		if strings.HasPrefix(arg, "-") {
			return q, errors.New(commandUsage)
		}
	}
	if q.History != 0 {
		if len(args) > 1 {
			return q, errors.New(commandUsage)
		}
		if len(args) == 1 {
			q.Selector = args[0]
		}
		return q, nil
	}
	switch len(args) {
	case 0:
	case 1:
		if isRelease(args[0]) {
			q.Release = args[0]
		} else {
			q.Selector = args[0]
		}
	case 2:
		if !isRelease(args[0]) {
			return q, errors.New(commandUsage)
		}
		q.Release, q.Selector = args[0], args[1]
	default:
		return q, errors.New(commandUsage)
	}
	return q, nil
}
func parseRuntimeDownload(args []string) (runtimeDownloadRequest, error) {
	r := runtimeDownloadRequest{Release: "latest"}
	for _, arg := range args {
		if strings.HasPrefix(arg, "-") {
			return r, errors.New(commandUsage)
		}
	}
	switch len(args) {
	case 1:
		if looksLikeAsset(args[0]) {
			r.AssetID = args[0]
		} else {
			r.Selector = args[0]
		}
	case 2:
		if !isRelease(args[0]) {
			return r, errors.New(commandUsage)
		}
		r.Release, r.Selector = args[0], args[1]
	default:
		return r, errors.New(commandUsage)
	}
	return r, nil
}
func isRelease(v string) bool {
	return v == "latest" || (len(v) > 1 && v[0] == 'b' && allDigits(v[1:]))
}
func allDigits(v string) bool {
	for _, r := range v {
		if r < '0' || r > '9' {
			return false
		}
	}
	return v != ""
}
func looksLikeAsset(v string) bool {
	return strings.HasPrefix(v, "llama-") && (strings.HasSuffix(v, ".zip") || strings.HasSuffix(v, ".tar.gz"))
}

func writeRuntimeText(w io.Writer, operation string, value any) error {
	switch v := value.(type) {
	case []runtimeRecord:
		if _, err := fmt.Fprintln(w, "RELEASE\tTARGET\tBACKEND\tASSET\tDEPENDENCIES"); err != nil {
			return fmt.Errorf("write runtime output: %w", err)
		}
		for _, r := range v {
			deps := strings.Join(r.Dependencies, ",")
			if deps == "" {
				deps = "-"
			}
			if _, err := fmt.Fprintf(w, "%s\t%s\t%s\t%s\t%s\n", r.Release, r.Target, r.Backend, r.AssetID, deps); err != nil {
				return fmt.Errorf("write runtime output: %w", err)
			}
		}
	case runtimeMutation:
		verb := "Downloaded"
		if operation == "remove" {
			verb = "Removed"
		}
		if !v.Changed {
			verb = "Already present"
		}
		if _, err := fmt.Fprintf(w, "%s %s\n", verb, v.Runtime.AssetID); err != nil {
			return fmt.Errorf("write runtime output: %w", err)
		}
	default:
		return errors.New("invalid runtime service result")
	}
	return nil
}
