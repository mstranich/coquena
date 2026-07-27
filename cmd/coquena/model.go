package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/signal"
	"strings"
	"text/tabwriter"

	downloadapp "github.com/mstranich/coquena/internal/download"
	"github.com/mstranich/coquena/internal/installation"
	modelapp "github.com/mstranich/coquena/internal/model"
	runtimeapp "github.com/mstranich/coquena/internal/runtime"
)

const modelUsage = "usage: coquena model resolve <repository-or-url> [--json]\n       coquena model inspect <repository-or-url> [--json]\n       coquena model download <repository-or-url> [--json]\n       coquena model download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]\n       coquena model revision <model-path> [--json]\n       coquena model list [--json]\n       coquena download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]"

// modelListEntry is the JSON-friendly, deterministic projection of an
// installed library entry reported by `coquena model list`.
type modelListEntry struct {
	Repository string `json:"repository"`
	Reference  string `json:"reference"`
	Filename   string `json:"filename"`
}

func newModelManager() (*modelapp.Manager, error) {
	models, e := installation.ModelsDirectory()
	if e != nil {
		return nil, e
	}
	manager := modelapp.NewManager(models)
	manager.LogWriter = os.Stderr
	return manager, nil
}

var modelManagerFactory = newModelManager
var downloadContextFactory = func() (context.Context, context.CancelFunc) {
	return signal.NotifyContext(context.Background(), os.Interrupt)
}

func runModel(args []string, out io.Writer) error {
	jsonOut, args, e := extractJSON(args)
	if e != nil || len(args) < 1 {
		return errors.New(modelUsage)
	}
	if args[0] == "list" {
		if len(args) != 1 {
			return errors.New(modelUsage)
		}
		m, e := modelManagerFactory()
		if e != nil {
			return e
		}
		entries, e := m.Library()
		if e != nil {
			return e
		}
		list := make([]modelListEntry, 0, len(entries))
		for _, entry := range entries {
			if entry.State != "installed" {
				continue
			}
			list = append(list, modelListEntry{
				Repository: entry.Source.Repository,
				Reference:  entry.Source.Repository + ":" + entry.Source.Filename,
				Filename:   entry.Source.Filename,
			})
		}
		if jsonOut {
			return json.NewEncoder(out).Encode(struct {
				SchemaVersion int              `json:"schemaVersion"`
				Result        []modelListEntry `json:"result"`
			}{1, list})
		}
		writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
		_, _ = fmt.Fprintln(writer, "REPOSITORY\tFILENAME")
		for _, entry := range list {
			_, _ = fmt.Fprintf(writer, "%s\t%s\n", entry.Repository, entry.Filename)
		}
		return writer.Flush()
	}
	if len(args) < 2 {
		return errors.New(modelUsage)
	}
	if args[0] == "download" && isDownloadAction(args[1]) {
		nested := append([]string(nil), args[1:]...)
		if jsonOut {
			nested = append(nested, "--json")
		}
		return runDownloadWithKind(nested, out, "model")
	}
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	var value any
	switch args[0] {
	case "resolve":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = m.Resolve(args[1], "")
	case "inspect":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		var c []modelapp.Candidate
		c, e = m.Resolve(args[1], "")
		if e == nil && len(c) == 1 {
			value, e = m.InspectRemote(c[0])
		} else if e == nil {
			e = errors.New("inspect requires one GGUF selection")
		}
	case "download":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		var c []modelapp.Candidate
		c, e = m.Resolve(args[1], "")
		if e == nil && len(c) == 1 {
			var j modelapp.Job
			j, e = m.Create(c[0])
			if e == nil {
				value, e = m.Resume(context.Background(), j.ID, nil)
			}
		} else if e == nil {
			e = errors.New("download requires one GGUF selection")
		}
	case "revision":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = modelapp.ReadRecord(args[1])
	default:
		return errors.New(modelUsage)
	}
	if e != nil {
		return e
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int `json:"schemaVersion"`
			Result        any `json:"result"`
		}{1, value})
	}
	switch v := value.(type) {
	case modelapp.Record:
		_, e = fmt.Fprintln(out, v.Source.ResolvedRevision)
	case modelapp.Job:
		_, e = fmt.Fprintln(out, v.ID)
	default:
		_, e = fmt.Fprintln(out, value)
	}
	return e
}

func isDownloadAction(value string) bool {
	switch value {
	case "list", "status", "pause", "resume", "cancel":
		return true
	default:
		return false
	}
}

func runDownload(args []string, out io.Writer) error {
	return runDownloadWithKind(args, out, "")
}

func runDownloadWithKind(args []string, out io.Writer, kind string) error {
	jsonOut, args, e := extractJSON(args)
	if e != nil || len(args) < 1 {
		return errors.New(modelUsage)
	}
	if kind == "runtime" && len(args) >= 1 && args[0] == "status" && len(args) == 2 {
		entries, e := listDownloads("runtime")
		if e != nil {
			return e
		}
		for _, entry := range entries.([]downloadapp.Entry) {
			if entry.ID == args[1] {
				if jsonOut {
					return json.NewEncoder(out).Encode(struct {
						SchemaVersion int `json:"schemaVersion"`
						Result        any `json:"result"`
					}{1, entry})
				}
				return writeDownloadHuman(out, entry, false)
			}
		}
		return os.ErrNotExist
	}
	if kind == "runtime" && len(args) >= 1 && args[0] != "list" && args[0] != "status" {
		return runRuntimeDownloadAction(args, out, jsonOut)
	}
	if kind == "" && len(args) == 2 && args[0] != "list" && args[0] != "status" {
		if entries, catalogErr := listDownloads("runtime"); catalogErr == nil {
			for _, entry := range entries.([]downloadapp.Entry) {
				if entry.ID == args[1] {
					return runRuntimeDownloadAction(args, out, jsonOut)
				}
			}
		}
	}
	if kind == "" && len(args) == 2 && args[0] == "status" {
		if entries, catalogErr := listDownloads("runtime"); catalogErr == nil {
			for _, entry := range entries.([]downloadapp.Entry) {
				if entry.ID == args[1] {
					return runDownloadWithKind([]string{"status", args[1]}, out, "runtime")
				}
			}
		}
	}
	if ((args[0] == "list" || args[0] == "status") && len(args) > 2) || (args[0] != "list" && args[0] != "status" && (!isDownloadAction(args[0]) || len(args) != 2)) {
		return errors.New(modelUsage)
	}
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	var value any
	interrupted := false
	switch args[0] {
	case "list":
		if len(args) != 1 {
			return errors.New(modelUsage)
		}
		value, e = listDownloads(kind)
	case "status":
		if len(args) == 1 {
			value, e = listDownloads(kind)
		} else {
			if entries, listErr := listDownloads(kind); listErr == nil {
				for _, entry := range entries.([]downloadapp.Entry) {
					if entry.ID == args[1] {
						value, e = entry, nil
						break
					}
				}
			}
			if value == nil {
				value, e = m.Status(args[1])
			}
		}
	case "pause":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = m.Pause(args[1])
	case "resume":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		ctx, stop := downloadContextFactory()
		value, e = m.Resume(ctx, args[1], nil)
		interrupted = ctx.Err() != nil
		stop()
	case "cancel":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		e = m.Cancel(args[1])
		value = map[string]string{"id": args[1]}
	default:
		return errors.New(modelUsage)
	}
	if e != nil {
		return e
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int `json:"schemaVersion"`
			Result        any `json:"result"`
		}{1, value})
	}
	return writeDownloadHuman(out, value, interrupted)
}

func runRuntimeDownloadAction(args []string, out io.Writer, jsonOut bool) error {
	if len(args) != 2 {
		return errors.New(modelUsage)
	}
	entries, err := listDownloads("runtime")
	if err != nil {
		return err
	}
	var found *downloadapp.Entry
	for i := range entries.([]downloadapp.Entry) {
		if entries.([]downloadapp.Entry)[i].ID == args[1] {
			found = &entries.([]downloadapp.Entry)[i]
			break
		}
	}
	if found == nil {
		return os.ErrNotExist
	}
	result := any(*found)
	switch args[0] {
	case "pause":
		var state map[string]any
		b, e := os.ReadFile(found.Path)
		if e != nil {
			return e
		}
		if e = json.Unmarshal(b, &state); e != nil {
			return e
		}
		state["status"] = "paused"
		b, _ = json.MarshalIndent(state, "", "  ")
		if e = os.WriteFile(found.Path, append(b, '\n'), 0o644); e != nil {
			return e
		}
		result = state
	case "cancel":
		_ = os.Remove(found.Path)
		_ = os.Remove(strings.TrimSuffix(found.Path, ".download.json") + ".partial")
		result = map[string]string{"id": found.ID, "type": "runtime", "status": "cancelled"}
	case "resume":
		manager := runtimeapp.NewManager()
		mutation, e := manager.Download(context.Background(), runtimeapp.DownloadRequest{AssetID: found.ID}, nil)
		if e != nil {
			return e
		}
		result = mutation
	default:
		return errors.New(modelUsage)
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int `json:"schemaVersion"`
			Result        any `json:"result"`
		}{1, result})
	}
	return writeDownloadHuman(out, result, false)
}

func writeDownloadHuman(out io.Writer, value any, interrupted bool) error {
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	writeJob := func(job modelapp.Job) {
		progress := "—"
		if job.ExpectedSizeBytes > 0 {
			progress = fmt.Sprintf("%.1f%% (%s / %s)", modelapp.DownloadPercent(job.DownloadedBytes, job.ExpectedSizeBytes), modelapp.HumanBytes(job.DownloadedBytes), modelapp.HumanBytes(job.ExpectedSizeBytes))
		}
		_, _ = fmt.Fprintf(writer, "%s\tmodel\t%s:%s\t%s\t%s\t%s\n", job.ID, job.Source.Repository, job.Source.Filename, job.Status, progress, job.TargetPath)
	}
	switch jobs := value.(type) {
	case downloadapp.Entry:
		_, _ = fmt.Fprintln(writer, "ID\tTYPE\tNAME\tSTATUS\tPROGRESS\tPATH")
		progress := "—"
		if jobs.Total > 0 {
			progress = fmt.Sprintf("%.1f%% (%s / %s)", modelapp.DownloadPercent(jobs.Progress, jobs.Total), modelapp.HumanBytes(jobs.Progress), modelapp.HumanBytes(jobs.Total))
		}
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\t%s\t%s\n", jobs.ID, jobs.Type, jobs.Name, jobs.Status, progress, jobs.Path)
	case []downloadapp.Entry:
		_, _ = fmt.Fprintln(writer, "ID\tTYPE\tNAME\tSTATUS\tPROGRESS\tPATH")
		for _, job := range jobs {
			progress := "—"
			if job.Total > 0 {
				progress = fmt.Sprintf("%.1f%% (%s / %s)", modelapp.DownloadPercent(job.Progress, job.Total), modelapp.HumanBytes(job.Progress), modelapp.HumanBytes(job.Total))
			}
			_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\t%s\t%s\n", job.ID, job.Type, job.Name, job.Status, progress, job.Path)
		}
	case []modelapp.Job:
		_, _ = fmt.Fprintln(writer, "ID\tTYPE\tNAME\tSTATUS\tPROGRESS\tPATH")
		for _, job := range jobs {
			writeJob(job)
		}
	case modelapp.Job:
		_, _ = fmt.Fprintln(writer, "ID\tTYPE\tNAME\tSTATUS\tPROGRESS\tPATH")
		writeJob(jobs)
	default:
		_, _ = fmt.Fprintln(writer, value)
	}
	if err := writer.Flush(); err != nil {
		return err
	}
	if interrupted {
		_, err := fmt.Fprintln(out, "Download paused after interrupt.")
		return err
	}
	return nil
}

func listDownloads(kind string) (any, error) {
	models, err := installation.ModelsDirectory()
	if err != nil {
		m, managerErr := modelManagerFactory()
		if managerErr != nil {
			return nil, err
		}
		jobs, listErr := m.List()
		if listErr != nil {
			return nil, listErr
		}
		entries := make([]downloadapp.Entry, 0, len(jobs))
		for _, job := range jobs {
			entries = append(entries, downloadapp.Entry{ID: job.ID, Type: "model", Name: job.Source.Repository + ":" + job.Source.Filename, Status: string(job.Status), Progress: job.DownloadedBytes, Total: job.ExpectedSizeBytes, Path: job.TargetPath})
		}
		if kind == "runtime" {
			return []downloadapp.Entry{}, nil
		}
		return entries, nil
	}
	runtimes, err := installation.RuntimesDirectory()
	if err != nil {
		return nil, err
	}
	entries, err := downloadapp.List(models, runtimes)
	if err != nil {
		return nil, err
	}
	if kind == "" {
		return entries, nil
	}
	filtered := entries[:0]
	for _, e := range entries {
		if e.Type == kind {
			filtered = append(filtered, e)
		}
	}
	return filtered, nil
}
