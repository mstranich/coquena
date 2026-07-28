package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/signal"
	"sort"
	"strings"
	"text/tabwriter"

	downloadapp "github.com/mstranich/coquena/internal/download"
	"github.com/mstranich/coquena/internal/installation"
	modelapp "github.com/mstranich/coquena/internal/model"
	runtimeapp "github.com/mstranich/coquena/internal/runtime"
)

const modelUsage = "usage: coquena model resolve <repository-or-url> [--json]\n       coquena model discover <repository-or-url[:selector]> [--json]\n       coquena model inspect <reference> [--json]\n       coquena model download <repository-or-url[:selector]> [--with-companions] [--json]\n       coquena model download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]\n       coquena model revision <reference> [--json]\n       coquena model list [--json]\n       coquena model reconcile (<reference>|--all) (--online|--accept) [--json]\n       coquena model verify <reference> [--json]\n       coquena model check-updates <reference> [--json]\n       coquena download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]"

// companionFlag is the opt-in that additionally enqueues the companions
// identified for the selected principal. The default stays one artifact per
// invocation, so downloading a companion is always something the operator
// asked for by name.
const companionFlag = "--with-companions"

// modelListEntry is the JSON-friendly, deterministic projection of a
// library entry reported by `coquena model list`. Companions holds the
// artifacts nested beneath this entry, so the same shape carries the tree in
// both human and JSON output.
type modelListEntry struct {
	State      string           `json:"state"`
	Repository string           `json:"repository"`
	Reference  string           `json:"reference"`
	Filename   string           `json:"filename"`
	Type       string           `json:"type,omitempty"`
	Companions []modelListEntry `json:"companions,omitempty"`
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

// runModel dispatches the model command family, translating a resolver
// diagnostic that carries candidates into one naming every filename. The
// wrapper sits at the single choke point every model subcommand returns
// through, so no command that resolves a selector can report only a count.
func runModel(args []string, out io.Writer) error {
	return withCandidateNames(runModelCommand(args, out))
}

func runModelCommand(args []string, out io.Writer) error {
	jsonOut, args, e := extractJSON(args)
	if e != nil || len(args) < 1 {
		return errors.New(modelUsage)
	}
	if args[0] == "list" {
		if len(args) != 1 {
			return errors.New(modelUsage)
		}
		return runModelList(out, jsonOut)
	}
	if args[0] == "reconcile" {
		return runModelReconcile(args[1:], out, jsonOut)
	}
	if args[0] == "verify" {
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		return runModelVerify(args[1], out, jsonOut)
	}
	if args[0] == "check-updates" {
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		return runModelCheckUpdates(args[1], out, jsonOut)
	}
	withCompanions := false
	if args[0] == "download" {
		args, withCompanions = extractModelFlag(args, companionFlag)
	}
	if len(args) < 2 {
		return errors.New(modelUsage)
	}
	if args[0] == "download" && isDownloadAction(args[1]) {
		if withCompanions {
			return errors.New(modelUsage)
		}
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
	case "discover":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = m.Discover(args[1])
	case "inspect":
		// inspect is local-only: remote exploration is discover's job, so this
		// reads the artifact's own header from disk and issues no request. An
		// unreconciled artifact is not addressable here — reconciling it first
		// is what keeps one logical name meaning one coherent thing.
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		var entry modelapp.LibraryEntry
		if entry, e = m.ResolveInstalled(args[1]); e == nil {
			value, e = m.InspectLocal(entry.TargetPath)
		}
	case "download":
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		value, e = runModelDownload(m, args[1], withCompanions)
	case "revision":
		// A revision is recorded in the sidecar, so only an installed artifact
		// has one to report.
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		var entry modelapp.LibraryEntry
		if entry, e = m.ResolveInstalled(args[1]); e == nil {
			value, e = modelapp.ReadRecord(entry.TargetPath)
		}
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
	case modelapp.Inspection:
		e = writeInspectionHuman(out, v)
	case modelapp.Discovery:
		e = writeDiscoveryHuman(out, v)
	case modelapp.Job:
		_, e = fmt.Fprintln(out, v.ID)
	case []modelapp.Job:
		for _, job := range v {
			if _, e = fmt.Fprintln(out, job.ID); e != nil {
				return e
			}
		}
	default:
		_, e = fmt.Fprintln(out, value)
	}
	return e
}

// extractModelFlag removes one boolean flag from args, reporting whether it was
// present. It mirrors extractJSON, which the model commands already use to keep
// flag position free.
func extractModelFlag(args []string, name string) ([]string, bool) {
	out := make([]string, 0, len(args))
	found := false
	for _, arg := range args {
		if arg == name {
			found = true
			continue
		}
		out = append(out, arg)
	}
	return out, found
}

// withCandidateNames augments a resolver diagnostic with the complete list of
// candidate filenames it carries. The error's own sentence caps the names it
// renders, but the CLI holds the structured slices, so it can list every one:
// an operator facing an ambiguous selector needs the names to choose from, not
// how many there were.
func withCandidateNames(err error) error {
	var selector *modelapp.ErrSelectorCandidates
	if errors.As(err, &selector) {
		candidates := selector.Matched
		if len(candidates) == 0 {
			candidates = selector.Available
		}
		return appendCandidateNames(err, candidates)
	}
	var ambiguous *modelapp.ErrAmbiguousInstalledMatch
	if errors.As(err, &ambiguous) {
		return appendCandidateNames(err, ambiguous.Matches)
	}
	var missing *modelapp.ErrNoInstalledMatch
	if errors.As(err, &missing) {
		return appendCandidateNames(err, missing.Available)
	}
	return err
}

func appendCandidateNames(err error, candidates []string) error {
	if len(candidates) == 0 {
		return err
	}
	var b strings.Builder
	b.WriteString(err.Error())
	b.WriteString("\navailable GGUF artifacts:")
	for _, candidate := range candidates {
		b.WriteString("\n  ")
		b.WriteString(candidate)
	}
	return errors.New(b.String())
}

// runModelDownload creates and runs the job for the selected artifact, plus one
// independent job per identified companion when the operator opted in. Each job
// is created through the same path a lone download uses, so every one of them
// stays independently pausable, resumable, and cancellable.
func runModelDownload(m *modelapp.Manager, reference string, withCompanions bool) (any, error) {
	candidates, err := m.Resolve(reference, "")
	if err != nil {
		return nil, err
	}
	if len(candidates) != 1 {
		return nil, errors.New("download requires one GGUF selection")
	}
	principal := candidates[0]
	if !withCompanions {
		return runOneDownload(m, principal)
	}
	companions, err := m.RemoteCompanions(principal)
	if err != nil {
		return nil, err
	}
	if len(companions) == 0 {
		// Reported on stderr so a --json invocation still emits exactly one
		// document on stdout.
		_, _ = fmt.Fprintf(os.Stderr, "no companion identified for %s:%s\n", principal.Source.Repository, principal.Source.Filename)
	}
	jobs := make([]modelapp.Job, 0, len(companions)+1)
	for _, candidate := range append([]modelapp.Candidate{principal}, companions...) {
		job, downloadErr := runOneDownload(m, candidate)
		if downloadErr != nil {
			return nil, downloadErr
		}
		jobs = append(jobs, job)
	}
	return jobs, nil
}

func runOneDownload(m *modelapp.Manager, candidate modelapp.Candidate) (modelapp.Job, error) {
	job, err := m.Create(candidate)
	if err != nil {
		return modelapp.Job{}, err
	}
	return m.Resume(context.Background(), job.ID, nil)
}

// writeInspectionHuman renders the inspection as the two-column table the
// specification requires, opening with the declared type so the operator reads
// what the artifact says it is before its metadata. Keys are sorted so two runs
// over the same artifact produce identical output.
func writeInspectionHuman(out io.Writer, inspection modelapp.Inspection) error {
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "METADATA\tVALUE")
	_, _ = fmt.Fprintf(writer, "classification.type\t%s\n", inspection.Classification.Type)
	if inspection.Classification.Architecture != "" {
		_, _ = fmt.Fprintf(writer, "classification.architecture\t%s\n", inspection.Classification.Architecture)
	}
	if inspection.Classification.SizeLabel != "" {
		_, _ = fmt.Fprintf(writer, "classification.sizeLabel\t%s\n", inspection.Classification.SizeLabel)
	}
	keys := make([]string, 0, len(inspection.Metadata))
	for key := range inspection.Metadata {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	for _, key := range keys {
		_, _ = fmt.Fprintf(writer, "%s\t%v\n", key, inspection.Metadata[key])
	}
	return writer.Flush()
}

// writeDiscoveryHuman reports every candidate the repository holds with its
// declared type, marking the one the selector resolves to rather than reducing
// the list to it, then prints the model card as plain text. Rendering styled
// markdown is explicitly out of scope.
func writeDiscoveryHuman(out io.Writer, discovery modelapp.Discovery) error {
	if _, err := fmt.Fprintf(out, "%s @ %s\n", discovery.Repository, discovery.Revision); err != nil {
		return err
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "\tTYPE\tARCHITECTURE\tSIZE\tFILENAME")
	for _, candidate := range discovery.Candidates {
		mark := " "
		if candidate.Selected {
			mark = "*"
		}
		size := "—"
		if candidate.Artifact.SizeBytes > 0 {
			size = modelapp.HumanBytes(candidate.Artifact.SizeBytes)
		}
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\t%s\n", mark, candidate.Classification.Type, candidate.Classification.Architecture, size, candidate.Source.Filename)
	}
	if err := writer.Flush(); err != nil {
		return err
	}
	if discovery.Selector != "" {
		if _, err := fmt.Fprintf(out, "* selector %q resolves here\n", discovery.Selector); err != nil {
			return err
		}
	}
	if discovery.ModelCard == "" {
		return nil
	}
	_, err := fmt.Fprintf(out, "\nModel card:\n%s\n", strings.TrimRight(discovery.ModelCard, "\n"))
	return err
}

// runModelList renders the library as a tree nesting each companion beneath the
// principal it is compatible with. A companion admissible for several
// principals is nested beneath the first of them in library order rather than
// repeated, so every artifact appears exactly once; one matching no installed
// principal stays at the top level rather than being omitted.
func runModelList(out io.Writer, jsonOut bool) error {
	m, err := modelManagerFactory()
	if err != nil {
		return err
	}
	entries, err := m.Library()
	if err != nil {
		return err
	}
	type libraryItem struct {
		entry          modelListEntry
		classification modelapp.Classification
	}
	items := make([]libraryItem, 0, len(entries))
	for _, entry := range entries {
		if entry.State != "installed" && entry.State != "unreconciled" {
			continue
		}
		item := libraryItem{entry: modelListEntry{
			State:      entry.State,
			Repository: entry.Source.Repository,
			Reference:  entry.Source.Repository + ":" + entry.Source.Filename,
			Filename:   entry.Source.Filename,
		}}
		// The classification comes from the sidecar, which already holds it and
		// which Library has already read: listing must never re-read the .gguf,
		// and it never reconciles.
		if entry.Classification != nil {
			item.classification = *entry.Classification
			item.entry.Type = string(entry.Classification.Type)
		}
		items = append(items, item)
	}
	nested := make([]bool, len(items))
	for companion := range items {
		if items[companion].classification.Type != modelapp.TypeProjector {
			continue
		}
		for principal := range items {
			if items[principal].classification.Type != modelapp.TypeModel {
				continue
			}
			if modelapp.ProjectorCriterion(items[principal].classification).Evaluate(items[companion].classification) != modelapp.Admissible {
				continue
			}
			items[principal].entry.Companions = append(items[principal].entry.Companions, items[companion].entry)
			nested[companion] = true
			break
		}
	}
	list := make([]modelListEntry, 0, len(items))
	for i, item := range items {
		if nested[i] {
			continue
		}
		list = append(list, item.entry)
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int              `json:"schemaVersion"`
			Result        []modelListEntry `json:"result"`
		}{1, list})
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "STATE\tTYPE\tREPOSITORY\tFILENAME")
	for _, entry := range list {
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\n", entry.State, entry.Type, entry.Repository, entry.Filename)
		for _, companion := range entry.Companions {
			_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\n", companion.State, companion.Type, companion.Repository, "  └─ "+companion.Filename)
		}
	}
	return writer.Flush()
}

func runModelReconcile(args []string, out io.Writer, jsonOut bool) error {
	var reference string
	var online, accept, all bool
	for _, a := range args {
		switch a {
		case "--online":
			online = true
		case "--accept":
			accept = true
		case "--all":
			all = true
		default:
			if reference != "" || strings.HasPrefix(a, "--") {
				return errors.New(modelUsage)
			}
			reference = a
		}
	}
	// --all replaces the reference rather than qualifying it, and neither mode
	// relaxes the rule that exactly one of --online/--accept is required.
	if online == accept || all == (reference != "") {
		return errors.New(modelUsage)
	}
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	if all {
		mode := modelapp.ReconcileModeAccept
		if online {
			mode = modelapp.ReconcileModeOnline
		}
		return runModelReconcileAll(m, mode, out, jsonOut)
	}
	// Reconciliation names its subject with the same user/repo:QUANT reference
	// every other model command accepts. The artifact is by definition not yet
	// installed, so the reference resolves against the unreconciled entries the
	// listing reports.
	entry, e := m.ResolveUnreconciled(reference)
	if e != nil {
		return e
	}
	var result modelapp.ReconcileResult
	if online {
		result, e = m.ReconcileOnline(entry.TargetPath)
	} else {
		result, e = m.ReconcileAccept(entry.TargetPath)
	}
	if e != nil {
		return e
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int                      `json:"schemaVersion"`
			Result        modelapp.ReconcileResult `json:"result"`
		}{1, result})
	}
	msg := fmt.Sprintf("Reconciled %s:%s.", result.Record.Source.Repository, result.Record.Source.Filename)
	if result.Partial {
		msg += " (partial: Hugging Face reported no content hash to compare against)"
	}
	_, e = fmt.Fprintln(out, msg)
	return e
}

// runModelReconcileAll renders a whole bulk pass: every artifact's outcome and
// the companion relations computed among them. A per-artifact failure is part
// of the reported result, not an error, because the pass deliberately continues
// past one bad file.
func runModelReconcileAll(m *modelapp.Manager, mode modelapp.ReconcileMode, out io.Writer, jsonOut bool) error {
	result, err := m.ReconcileAll(mode)
	if err != nil {
		return err
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int                          `json:"schemaVersion"`
			Result        modelapp.BulkReconcileResult `json:"result"`
		}{1, result})
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "OUTCOME\tTYPE\tREPOSITORY\tFILENAME\tDIAGNOSTIC")
	for _, entry := range result.Entries {
		diagnostic := entry.Diagnostic
		if entry.Partial {
			diagnostic = "partial: Hugging Face reported no content hash to compare against"
		}
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\t%s\n", entry.Outcome, entry.Classification.Type, entry.Repository, entry.Filename, diagnostic)
	}
	if err = writer.Flush(); err != nil {
		return err
	}
	if len(result.Relations) == 0 {
		_, err = fmt.Fprintf(out, "\n%d artifacts, no companion relation computed.\n", len(result.Entries))
		return err
	}
	if _, err = fmt.Fprintf(out, "\n%d artifacts, %d companion relations:\n", len(result.Entries), len(result.Relations)); err != nil {
		return err
	}
	for _, relation := range result.Relations {
		if _, err = fmt.Fprintf(out, "  %s %s -> %s\n", relation.Role, relation.CompanionPath, relation.PrincipalPath); err != nil {
			return err
		}
	}
	return nil
}

func runModelVerify(reference string, out io.Writer, jsonOut bool) error {
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	result, e := m.Verify(reference)
	if e != nil {
		return e
	}
	if jsonOut {
		if e = json.NewEncoder(out).Encode(struct {
			SchemaVersion int                   `json:"schemaVersion"`
			Result        modelapp.VerifyResult `json:"result"`
		}{1, result}); e != nil {
			return e
		}
	} else {
		var msg string
		switch result.Outcome {
		case modelapp.VerifyMatched:
			msg = fmt.Sprintf("%s:%s verified: content hash matches the recorded baseline.", result.Repository, result.Filename)
		case modelapp.VerifyMismatched:
			msg = fmt.Sprintf("%s:%s FAILED verification: content hash mismatch. The model is unusable until it is re-downloaded or reconciled again.", result.Repository, result.Filename)
		case modelapp.VerifyNoBaseline:
			msg = fmt.Sprintf("%s:%s: no baseline content hash recorded to verify against.", result.Repository, result.Filename)
		}
		if _, e = fmt.Fprintln(out, msg); e != nil {
			return e
		}
	}
	if result.Outcome == modelapp.VerifyMismatched {
		return fmt.Errorf("model %s:%s failed verification: recorded sha256 %s, computed sha256 %s", result.Repository, result.Filename, result.RecordedSHA256, result.ComputedSHA256)
	}
	return nil
}

func runModelCheckUpdates(reference string, out io.Writer, jsonOut bool) error {
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	result, e := m.CheckUpdates(reference)
	if e != nil {
		return e
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int                         `json:"schemaVersion"`
			Result        modelapp.CheckUpdatesResult `json:"result"`
		}{1, result})
	}
	var msg string
	switch result.Outcome {
	case modelapp.UpdateCurrent:
		msg = fmt.Sprintf("%s:%s is up to date.", result.Repository, result.Filename)
	case modelapp.UpdateAvailable:
		msg = fmt.Sprintf("%s:%s: update available (new revision %s).", result.Repository, result.Filename, result.CurrentRevision)
	case modelapp.UpdatePartial:
		msg = fmt.Sprintf("%s:%s: partial comparison only (Hugging Face reported no content hash); current commit revision is %s.", result.Repository, result.Filename, result.CurrentRevision)
	}
	_, e = fmt.Fprintln(out, msg)
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
