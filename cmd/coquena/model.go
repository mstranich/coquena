package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/signal"
	"text/tabwriter"

	"github.com/mstranich/coquena/internal/installation"
	modelapp "github.com/mstranich/coquena/internal/model"
)

const modelUsage = "usage: coquena model resolve <repository-or-url> [--json]\n       coquena model inspect <repository-or-url> [--json]\n       coquena model download <repository-or-url> [--json]\n       coquena model download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]\n       coquena model revision <model-path> [--json]\n       coquena download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]"

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
	if e != nil || len(args) < 2 {
		return errors.New(modelUsage)
	}
	if args[0] == "download" && isDownloadAction(args[1]) {
		nested := append([]string(nil), args[1:]...)
		if jsonOut {
			nested = append(nested, "--json")
		}
		return runDownload(nested, out)
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
	jsonOut, args, e := extractJSON(args)
	if e != nil || len(args) < 1 {
		return errors.New(modelUsage)
	}
	if (args[0] == "list" && len(args) != 1) || (args[0] != "list" && (!isDownloadAction(args[0]) || len(args) != 2)) {
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
		value, e = m.List()
	case "status":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = m.Status(args[1])
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

func writeDownloadHuman(out io.Writer, value any, interrupted bool) error {
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	writeJob := func(job modelapp.Job) {
		progress := "—"
		if job.ExpectedSizeBytes > 0 {
			progress = fmt.Sprintf("%.1f%% (%s / %s)", modelapp.DownloadPercent(job.DownloadedBytes, job.ExpectedSizeBytes), modelapp.HumanBytes(job.DownloadedBytes), modelapp.HumanBytes(job.ExpectedSizeBytes))
		}
		_, _ = fmt.Fprintf(writer, "%s\t%s:%s\t%s\t%s\t%s\n", job.ID, job.Source.Repository, job.Source.Filename, job.Status, progress, job.TargetPath)
	}
	switch jobs := value.(type) {
	case []modelapp.Job:
		_, _ = fmt.Fprintln(writer, "ID\tMODEL\tSTATUS\tPROGRESS\tPATH")
		for _, job := range jobs {
			writeJob(job)
		}
	case modelapp.Job:
		_, _ = fmt.Fprintln(writer, "ID\tMODEL\tSTATUS\tPROGRESS\tPATH")
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
