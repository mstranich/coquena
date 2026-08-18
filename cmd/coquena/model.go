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

const modelUsage = "usage: coquena model resolve <repository-or-url> [--json]\n       coquena model discover <repository-or-url[:selector]> [--model-card] [--json]\n       coquena model download <repository-or-url[:selector]> [--with-companions] [--json]\n       coquena model download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]\n       coquena model revision <reference> [--json]\n       coquena model list [--json]\n       coquena model reconcile <reference> (--online|--accept)\n       coquena model verify <reference> [--repair-derived] [--json]\n       coquena model check-updates <reference> [--json]\n       coquena download list|status <job-id>|pause <job-id>|resume <job-id>|cancel <job-id> [--json]"

// companionFlag is the opt-in that additionally enqueues the companions
// identified for the selected principal. The default stays one artifact per
// invocation, so downloading a companion is always something the operator
// asked for by name.
const companionFlag = "--with-companions"
const modelCardFlag = "--model-card"

// modelListEntry is the JSON-friendly, deterministic projection of a
// library entry reported by `coquena model list`. Every artifact is reported at
// the same level: a projector is compatible with every quantization of its
// principal, because the criterion is embedding length and that does not change
// between quants, so nesting one beneath another would assert a relation the
// domain does not have.
type modelListEntry struct {
	State            string `json:"state"`
	Repository       string `json:"repository"`
	Reference        string `json:"reference"`
	LogicalSelector  string `json:"logicalSelector,omitempty"`
	ResolvedRevision string `json:"resolvedRevision,omitempty"`
	Quant            string `json:"quant"`
	Filename         string `json:"filename"`
	Type             string `json:"type,omitempty"`
	CountParameters  int64  `json:"countParameters"`
	DerivationStale  bool   `json:"derivationStale,omitempty"`
	// Usable is the derived usability, flattened into the entry so both output
	// modes render it without consulting anything else.
	Usable bool `json:"usable"`
	// UnusableReason lets a caller branch on *why* an artifact is ineligible.
	// Altered bytes and an absent sidecar have different remedies, and a caller
	// that can only see `usable: false` cannot tell them apart.
	UnusableReason string `json:"unusableReason,omitempty"`
	// Advice is a note that does not bear on eligibility, such as a companion
	// having no compatible principal installed yet.
	Advice string `json:"advice,omitempty"`
	// Standing and AttestationMethod carry the attestation distinction in
	// structured output. They deliberately get no column in the human listing:
	// most artifacts sit at one value, and the state vocabulary that will
	// render them is `adopt-artifact-state-vocabulary`.
	Standing          string `json:"standing"`
	AttestationMethod string `json:"attestationMethod,omitempty"`
	// Verification reports what the last integrity check found and when, read
	// from the record rather than by hashing. Stale means the file was touched
	// after the check, which weakens the result without changing it.
	VerificationOutcome   string `json:"verificationOutcome,omitempty"`
	VerificationCheckedAt string `json:"verificationCheckedAt,omitempty"`
	VerificationStale     bool   `json:"verificationStale,omitempty"`
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
		verifyArgs, repairDerived := extractModelFlag(args, "--repair-derived")
		if len(verifyArgs) != 2 || verifyArgs[1] == "--all" {
			return errors.New(modelUsage)
		}
		return runModelVerify(verifyArgs[1], repairDerived, out, jsonOut)
	}
	if args[0] == "check-updates" {
		if len(args) != 2 {
			return errors.New(modelUsage)
		}
		return runModelCheckUpdates(args[1], out, jsonOut)
	}
	withCompanions := false
	includeModelCard := false
	if args[0] == "download" {
		args, withCompanions = extractModelFlag(args, companionFlag)
	}
	if args[0] == "discover" {
		args, includeModelCard = extractModelFlag(args, modelCardFlag)
	} else if _, found := extractModelFlag(args, modelCardFlag); found {
		return errors.New(modelUsage)
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
	switch args[0] {
	case "resolve", "discover", "download", "revision":
	default:
		return errors.New(modelUsage)
	}
	if len(args) != 2 {
		return errors.New(modelUsage)
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
		value, e = m.DiscoverWithOptions(args[1], modelapp.DiscoverOptions{IncludeModelCard: includeModelCard})
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
		candidates := selector.References
		if len(candidates) == 0 {
			candidates = selector.Matched
			if len(candidates) == 0 {
				candidates = selector.Available
			}
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

// writeDiscoveryHuman leads with the quantization comparison discovery exists
// to answer. Artifact-level diagnostics remain attached to filenames, while
// shared companions are shown separately so their bytes are never charged to
// every quantization.
func writeDiscoveryHuman(out io.Writer, discovery modelapp.Discovery) error {
	if _, err := fmt.Fprintf(out, "%s @ %s\n", discovery.Repository, discovery.Revision); err != nil {
		return err
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "\tTYPE\tQUANTIZATION\tTOTAL\tFILE")
	for _, quantization := range discovery.Quantizations {
		mark := " "
		if quantization.Selected {
			mark = "*"
		}
		total := "—"
		if quantization.TotalSizeKnown {
			total = modelapp.HumanBytes(quantization.TotalSizeBytes)
		}
		for i, candidate := range quantization.Files {
			label, displayedTotal := "", ""
			if i == 0 {
				label, displayedTotal = quantization.Label, total
			}
			filename := candidate.Source.Filename
			if candidate.Diagnostic != "" {
				filename += " (diagnostic: " + candidate.Diagnostic + ")"
			}
			typeName := ""
			if i == 0 {
				typeName = string(modelapp.TypeModel)
			}
			_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\t%s\n", mark, typeName, label, displayedTotal, filename)
			mark = " "
		}
	}
	if len(discovery.Companions) > 0 {
		_, _ = fmt.Fprintln(writer, "\nCOMPANIONS\tTYPE\tSIZE\tFILE")
		for _, companion := range discovery.Companions {
			size := "—"
			if companion.Artifact.SizeBytes > 0 {
				size = modelapp.HumanBytes(companion.Artifact.SizeBytes)
			}
			filename := companion.Source.Filename
			if companion.Diagnostic != "" {
				filename += " (diagnostic: " + companion.Diagnostic + ")"
			}
			_, _ = fmt.Fprintf(writer, "\t%s\t%s\t%s\n", companion.Classification.Type, size, filename)
		}
	}
	if err := writer.Flush(); err != nil {
		return err
	}
	if discovery.Selector != "" {
		if _, err := fmt.Fprintf(out, "* selector %q resolves here\n", discovery.Selector); err != nil {
			return err
		}
	}
	if discovery.ModelCard != "" {
		if _, err := fmt.Fprintf(out, "\nModel card:\n%s\n", strings.TrimRight(discovery.ModelCard, "\r\n")); err != nil {
			return err
		}
	}
	return nil
}

// typePriority maps artifact types to their sort priority. Lower values sort first.
var typePriority = map[string]int{
	"model":   0,
	"mmproj":  1,
	"lora":    2,
	"draft":   3,
	"vocoder": 4,
}

// statePriority maps library states to their sort priority. Lower values sort first.
var statePriority = map[string]int{
	"installed":    0,
	"unreconciled": 1,
}

// computeQuant derives the QUANT value for an entry. For model artifacts it's
// just the quantization type; for mmproj it's architecture:quantization.
func computeQuant(entry modelapp.LibraryEntry) string {
	if entry.Classification == nil {
		return "unknown"
	}
	if entry.Classification.Type == modelapp.TypeProjector && entry.Classification.Architecture != "" {
		if entry.Classification.FileType != nil {
			return entry.Classification.Architecture + ":" + modelapp.LlamaFileTypeDisplayName(*entry.Classification.FileType)
		}
		if entry.PhysicalQuantization != nil {
			return entry.Classification.Architecture + ":" + entry.PhysicalQuantization.Name
		}
		return entry.Classification.Architecture + ":unknown"
	}
	if entry.Classification.FileType != nil {
		return modelapp.LlamaFileTypeDisplayName(*entry.Classification.FileType)
	}
	if entry.PhysicalQuantization != nil {
		return entry.PhysicalQuantization.Name
	}
	return "unknown"
}

// verifyStandingSentence renders an artifact's attestation standing for a human.
//
// Each phrasing states what is known, never what is missing. An artifact
// obtained privately will sit at `unattested` forever and is not deficient for
// it; one whose repository published nothing Coquena can reproduce is `partial`
// and is not failing either.
func verifyStandingSentence(result modelapp.VerifyResult) string {
	switch result.Standing {
	case modelapp.StandingVerified:
		if result.AttestationMethod == modelapp.MethodDownload {
			return "verified (established during the download, from a revision-pinned URL)"
		}
		return "verified (established by comparing the local bytes against the repository)"
	case modelapp.StandingPartial:
		return "partial (a repository was consulted; no match could be established)"
	default:
		return "unattested (no repository has been consulted for this artifact)"
	}
}

// runModelList renders the library grouped by repository, with a QUANT column
// instead of REFERENCE. Entries within each repository are sorted by type
// priority, then state, then parameter count.
func runModelList(out io.Writer, jsonOut bool) error {
	m, err := modelManagerFactory()
	if err != nil {
		return err
	}
	entries, err := m.Library()
	if err != nil {
		return err
	}
	// Group by repository
	byRepo := map[string][]modelListEntry{}
	var repoOrder []string
	for _, entry := range entries {
		if entry.State != "installed" && entry.State != "unreconciled" {
			continue
		}
		repo := entry.Source.Repository
		if _, exists := byRepo[repo]; !exists {
			repoOrder = append(repoOrder, repo)
		}
		item := modelListEntry{
			State:             entry.State,
			Repository:        repo,
			Reference:         modelapp.CanonicalReference(entry.Source),
			LogicalSelector:   modelapp.LogicalReference(entry),
			ResolvedRevision:  entry.Source.ResolvedRevision,
			Quant:             computeQuant(entry),
			Filename:          entry.Source.Filename,
			DerivationStale:   entry.DerivationStale,
			Usable:            entry.Usability.Usable,
			UnusableReason:    string(entry.Usability.Reason),
			Advice:            entry.Usability.Advice,
			Standing:          string(entry.Standing),
			AttestationMethod: string(entry.AttestationMethod),
		}
		if entry.Verification != nil {
			item.VerificationOutcome = string(entry.Verification.Outcome)
			item.VerificationCheckedAt = entry.Verification.CheckedAt
			item.VerificationStale = entry.VerificationStale
		}
		if entry.Classification != nil {
			item.Type = string(entry.Classification.Type)
			item.CountParameters = entry.Classification.CountParameters
		}
		byRepo[repo] = append(byRepo[repo], item)
	}
	// Sort entries within each repository
	for _, items := range byRepo {
		sort.SliceStable(items, func(i, j int) bool {
			pi := typePriority[items[i].Type]
			pj := typePriority[items[j].Type]
			if pi != pj {
				return pi < pj
			}
			// State: installed before unreconciled
			si := statePriority[items[i].State]
			sj := statePriority[items[j].State]
			if si != sj {
				return si < sj
			}
			// Parameters: descending
			return items[i].CountParameters > items[j].CountParameters
		})
	}
	if jsonOut {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int                         `json:"schemaVersion"`
			Result        map[string][]modelListEntry `json:"result"`
		}{1, byRepo})
	}
	// Render grouped by repository with headers
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	for _, repo := range repoOrder {
		_, _ = fmt.Fprintf(writer, "REPOSITORY: %s\n", repo)
		_, _ = fmt.Fprintln(writer, "  STATE\tDERIVATION\tTYPE\tUSABLE\tREFERENCE\tQUANT SELECTOR")
		for _, entry := range byRepo[repo] {
			usable := "no"
			if entry.Usable {
				usable = "yes"
			}
			derivation := "current"
			if entry.DerivationStale {
				derivation = "stale"
			}
			_, _ = fmt.Fprintf(writer, "  %s\t%s\t%s\t%s\t%s\t%s\n", entry.State, derivation, entry.Type, usable, entry.Reference, entry.LogicalSelector)
		}
		_, _ = fmt.Fprintln(writer)
	}
	return writer.Flush()
}

// runModelReconcile parses one reconciliation invocation and refuses the two
// options this command no longer accepts, rather than ignoring them: an option
// that is silently dropped looks to the operator like it worked.
//
// --all is refused because reconciling the whole library conflates artifacts
// that need an operator decision with artifacts that do not. The behaviour is
// turned off here and reconsidered in a later change; see Manager.ReconcileAll.
//
// --json is refused because reconciliation prompts, and a command that prompts
// cannot emit exactly one clean JSON document. The rule is about interactivity,
// not about mutation. The flag is extracted globally before dispatch, so this
// is a refusal of something already parsed rather than an unrecognized option.
func runModelReconcile(args []string, out io.Writer, jsonOut bool) error {
	var reference string
	var online, accept bool
	for _, a := range args {
		switch a {
		case "--online":
			online = true
		case "--accept":
			accept = true
		case "--all":
			return errors.New("coquena model reconcile: --all is not accepted: reconciliation operates on one repository named by reference, never on the whole library. Reconcile each repository by name.")
		default:
			if reference != "" || strings.HasPrefix(a, "--") {
				return errors.New(modelUsage)
			}
			reference = a
		}
	}
	if jsonOut {
		return errors.New("coquena model reconcile: --json does not apply to this command: reconciliation prompts, and a command that prompts cannot emit exactly one JSON document.")
	}
	if online == accept || reference == "" {
		return errors.New(modelUsage)
	}
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	// --online reconciles the whole repository the reference names: a principal
	// and its projector are parts of one logical unit, and reconciling them one
	// at a time is what produces the half-reconciled library this command exists
	// to prevent.
	if online {
		return runModelReconcileRepository(m, reference, out)
	}
	// --accept stays one artifact per invocation: it asserts an identity from
	// the path alone with nothing to corroborate it against, so it is never
	// applied to a set the operator did not name one by one. The artifact is by
	// definition not yet installed, so the reference resolves against the
	// unreconciled entries the listing reports.
	entry, e := m.ResolveUnreconciled(reference)
	if e != nil {
		return e
	}
	result, e := m.ReconcileAccept(entry.TargetPath)
	if e != nil {
		return e
	}
	msg := fmt.Sprintf("Reconciled %s:%s.", result.Record.Source.Repository, result.Record.Source.Filename)
	if result.Partial() {
		msg += " (partial: Hugging Face reported no content hash to compare against)"
	}
	_, e = fmt.Fprintln(out, msg)
	return e
}

// runModelReconcileRepository renders one repository-driven reconciliation:
// every artifact the repository and the library between them account for, with
// what happened to it. An absent candidate and a foreign file are reported
// rather than omitted, since each names something the operator has to decide.
//
// A refusal does not stop the pass but does fail the invocation: the operator
// sees everything that was established, and the exit status still says the
// repository was not fully reconciled.
func runModelReconcileRepository(m *modelapp.Manager, reference string, out io.Writer) error {
	result, err := m.ReconcileRepository(reference)
	if err != nil {
		return err
	}
	if _, err = fmt.Fprintf(out, "%s at revision %s\n", result.Repository, result.Revision); err != nil {
		return err
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "OUTCOME\tTYPE\tFILENAME\tDIAGNOSTIC")
	for _, artifact := range result.Artifacts {
		diagnostic := artifact.Diagnostic
		if artifact.Partial {
			diagnostic = "partial: Hugging Face reported no content hash to compare against"
		}
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\t%s\n", artifact.Outcome, artifact.Classification.Type, artifact.Filename, diagnostic)
	}
	if err = writer.Flush(); err != nil {
		return err
	}
	refused := result.Refused()
	if len(refused) == 0 {
		return nil
	}
	names := make([]string, 0, len(refused))
	for _, artifact := range refused {
		names = append(names, artifact.Filename)
	}
	return fmt.Errorf("%s was not fully reconciled: %s refused", result.Repository, strings.Join(names, ", "))
}

func runModelVerify(reference string, repairDerived bool, out io.Writer, jsonOut bool) error {
	m, e := modelManagerFactory()
	if e != nil {
		return e
	}
	result, e := m.VerifyWithOptions(reference, modelapp.VerifyOptions{RepairDerived: repairDerived})
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
			msg = fmt.Sprintf("%s:%s verified: the bytes still match the digest recorded for them.", result.Repository, result.Filename)
		case modelapp.VerifyMismatched:
			msg = fmt.Sprintf("%s:%s FAILED verification: the bytes no longer match the digest recorded for them. The model is unusable until it is re-downloaded or reconciled again.", result.Repository, result.Filename)
		}
		if _, e = fmt.Fprintln(out, msg); e != nil {
			return e
		}
		if result.CheckedAt != "" {
			_, _ = fmt.Fprintf(out, "Recorded at: %s\n", result.CheckedAt)
		}
		if result.Stale {
			_, _ = fmt.Fprintln(out, "The artifact was modified after this check ran, so the result may already be out of date.")
		}
		// The standing answers a different question from the outcome: not
		// whether the bytes changed, but what is known about where they came
		// from. None of the three values is a failure.
		_, _ = fmt.Fprintf(out, "Attestation: %s\n", verifyStandingSentence(result))
		if result.DeclaredRecipe != "" {
			_, _ = fmt.Fprintf(out, "Declared recipe: %s\n", result.DeclaredRecipe)
		}
		if result.PhysicalQuantization != nil {
			_, _ = fmt.Fprintf(out, "Physical composition: %s (%d structural parameters)\n", result.PhysicalQuantization.Name, result.PhysicalQuantization.CountParameters)
		}
		freshness := "current"
		if result.DerivationStale {
			freshness = "stale"
		}
		_, _ = fmt.Fprintf(out, "Derivation: %s\n", freshness)
		if result.Repaired {
			_, _ = fmt.Fprintln(out, "Derived evidence repaired.")
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
