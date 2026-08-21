package presets

import (
	"errors"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/model"
)

// companionResolver resolves each reference to its own entry, which the
// companion path needs and the single-entry fakeResolver cannot express: every
// companion operation resolves the principal and the candidate in one call.
type companionResolver struct {
	entries map[string]model.LibraryEntry
}

func (r companionResolver) ResolveInstalled(reference string) (model.LibraryEntry, error) {
	entry, ok := r.entries[reference]
	if !ok {
		return model.LibraryEntry{}, &model.ErrNoInstalledMatch{Reference: reference}
	}
	return entry, nil
}

type companionLibrary struct {
	entries []model.LibraryEntry
	err     error
}

func (l companionLibrary) Library() ([]model.LibraryEntry, error) { return l.entries, l.err }

func companionPrincipalClassification() model.Classification {
	return model.Classification{
		Type:            model.TypeModel,
		Architecture:    "gemma4",
		EmbeddingLength: 5120,
		Tokenizer:       model.TokenizerDigest{Model: "gpt2", Pre: "gemma4", VocabSize: 262144, Digest: "d41d8cd9"},
	}
}

func companionProjectorClassification(dim int64) model.Classification {
	return model.Classification{Type: model.TypeProjector, Architecture: "clip", ProjectionDim: dim, HasVisionEncoder: true}
}

func companionEntry(repository, filename string, classification *model.Classification) model.LibraryEntry {
	return model.LibraryEntry{
		Source:         model.Source{Repository: repository, Filename: filename, ResolvedRevision: "rev-" + filename},
		TargetPath:     "/models/" + filename,
		State:          model.StateSynced,
		Classification: classification,
	}
}

const companionPrincipalRef = "google/Gemma4-GGUF:Q4_K_M"

func companionFixture(t *testing.T) (companionResolver, Preset) {
	t.Helper()
	principal := companionPrincipalClassification()
	resolver := companionResolver{entries: map[string]model.LibraryEntry{
		companionPrincipalRef: companionEntry("google/Gemma4-GGUF", "gemma4-Q4_K_M.gguf", &principal),
	}}
	preset := Preset{Name: "vision", Model: &ModelBinding{Requested: ModelReference{Raw: companionPrincipalRef}}}
	return resolver, preset
}

func TestBindCompanionStoresCompatibleProjector(t *testing.T) {
	resolver, preset := companionFixture(t)
	matching := companionProjectorClassification(5120)
	resolver.entries["google/Gemma4-GGUF:mmproj"] = companionEntry("google/Gemma4-GGUF", "mmproj-F16.gguf", &matching)

	assignment, err := BindCompanion(resolver, preset, model.RoleProjector, "google/Gemma4-GGUF:mmproj", false)
	if err != nil {
		t.Fatal(err)
	}
	if assignment.Role != model.RoleProjector {
		t.Fatalf("role=%q", assignment.Role)
	}
	if assignment.Requested.Raw != "google/Gemma4-GGUF:mmproj" || assignment.Requested.Repository != "google/Gemma4-GGUF" {
		t.Fatalf("requested=%+v", assignment.Requested)
	}
	if assignment.Resolved == nil || assignment.Resolved.Filename != "mmproj-F16.gguf" || assignment.Resolved.ResolvedRevision != "rev-mmproj-F16.gguf" {
		t.Fatalf("resolved=%+v", assignment.Resolved)
	}
	if diagnostic, ok := assignment.Diagnostic(); ok {
		t.Fatalf("unexpected diagnostic %q", diagnostic)
	}
}

func TestBindCompanionRefusesIncompatible(t *testing.T) {
	resolver, preset := companionFixture(t)
	mismatched := companionProjectorClassification(3840)
	resolver.entries["other/Proj-GGUF:mmproj"] = companionEntry("other/Proj-GGUF", "other-mmproj.gguf", &mismatched)

	_, err := BindCompanion(resolver, preset, model.RoleProjector, "other/Proj-GGUF:mmproj", false)
	if err == nil {
		t.Fatal("expected a refusal")
	}
	var refusal *ErrCompanionCriterion
	if !errors.As(err, &refusal) {
		t.Fatalf("err=%v", err)
	}
	if refusal.Criterion != CriterionName(model.RoleProjector) || refusal.Outcome != model.NotAdmissible {
		t.Fatalf("refusal=%+v", refusal)
	}
	if !strings.Contains(err.Error(), CriterionName(model.RoleProjector)) {
		t.Fatalf("diagnostic does not name the criterion: %v", err)
	}
}

func TestBindCompanionRefusesUnknownOutcome(t *testing.T) {
	resolver, preset := companionFixture(t)
	// A projector declaring no projection dimension leaves the criterion
	// undecidable, which is not the same as satisfying it.
	undeclared := model.Classification{Type: model.TypeProjector, Architecture: "clip"}
	resolver.entries["other/Proj-GGUF:mmproj"] = companionEntry("other/Proj-GGUF", "quiet-mmproj.gguf", &undeclared)

	_, err := BindCompanion(resolver, preset, model.RoleProjector, "other/Proj-GGUF:mmproj", false)
	var refusal *ErrCompanionCriterion
	if !errors.As(err, &refusal) {
		t.Fatalf("err=%v", err)
	}
	if refusal.Outcome != model.CompatibilityUnknown {
		t.Fatalf("outcome=%q", refusal.Outcome)
	}
}

func TestBindCompanionOverrideRecordsUnmetCriterion(t *testing.T) {
	resolver, preset := companionFixture(t)
	mismatched := companionProjectorClassification(3840)
	resolver.entries["other/Proj-GGUF:mmproj"] = companionEntry("other/Proj-GGUF", "other-mmproj.gguf", &mismatched)

	assignment, err := BindCompanion(resolver, preset, model.RoleProjector, "other/Proj-GGUF:mmproj", true)
	if err != nil {
		t.Fatal(err)
	}
	diagnostic, ok := assignment.Diagnostic()
	if !ok {
		t.Fatal("expected the overridden assignment to carry a diagnostic")
	}
	if !strings.Contains(diagnostic, CriterionName(model.RoleProjector)) {
		t.Fatalf("diagnostic=%q", diagnostic)
	}
	if assignment.Resolved == nil || assignment.Resolved.Filename != "other-mmproj.gguf" {
		t.Fatalf("resolved=%+v", assignment.Resolved)
	}
}

func TestBindCompanionOverrideStillRefusesUnresolvable(t *testing.T) {
	resolver, preset := companionFixture(t)
	if _, err := BindCompanion(resolver, preset, model.RoleProjector, "absent/Proj-GGUF:mmproj", true); err == nil {
		t.Fatal("expected an override to cover the criterion only")
	}
}

func TestBindCompanionRefusesUnreconciledCandidate(t *testing.T) {
	resolver, preset := companionFixture(t)
	resolver.entries["other/Proj-GGUF:mmproj"] = companionEntry("other/Proj-GGUF", "raw-mmproj.gguf", nil)
	if _, err := BindCompanion(resolver, preset, model.RoleProjector, "other/Proj-GGUF:mmproj", true); err == nil {
		t.Fatal("expected an unreconciled candidate to be refused even under override")
	}
}

func TestCompanionCriterionRefusesPresetWithNoModel(t *testing.T) {
	resolver, _ := companionFixture(t)
	preset := Preset{Name: "bare"}
	_, err := CompanionCriterion(resolver, preset, model.RoleProjector)
	if !errors.Is(err, ErrNoPrincipal) {
		t.Fatalf("err=%v", err)
	}
	if _, err := BindCompanion(resolver, preset, model.RoleProjector, "google/Gemma4-GGUF:mmproj", true); !errors.Is(err, ErrNoPrincipal) {
		t.Fatalf("expected an override not to conjure a criterion: %v", err)
	}
}

func TestCompanionCriterionRefusesUnreconciledPrincipal(t *testing.T) {
	resolver := companionResolver{entries: map[string]model.LibraryEntry{
		companionPrincipalRef: companionEntry("google/Gemma4-GGUF", "gemma4-Q4_K_M.gguf", nil),
	}}
	preset := Preset{Name: "vision", Model: &ModelBinding{Requested: ModelReference{Raw: companionPrincipalRef}}}
	if _, err := CompanionCriterion(resolver, preset, model.RoleProjector); !errors.Is(err, ErrNoPrincipal) {
		t.Fatalf("err=%v", err)
	}
}

func TestCompanionCriterionRefusesUnknownRole(t *testing.T) {
	resolver, preset := companionFixture(t)
	if _, err := CompanionCriterion(resolver, preset, model.CompanionRole("adapter")); err == nil {
		t.Fatal("expected an unknown role to be refused")
	}
}

func TestCompanionOptionsReportsSeveralWithoutChoosing(t *testing.T) {
	resolver, preset := companionFixture(t)
	principal := companionPrincipalClassification()
	first := companionProjectorClassification(5120)
	second := companionProjectorClassification(5120)
	mismatched := companionProjectorClassification(3840)
	library := companionLibrary{entries: []model.LibraryEntry{
		companionEntry("google/Gemma4-GGUF", "gemma4-Q4_K_M.gguf", &principal),
		companionEntry("google/Gemma4-GGUF", "mmproj-F16.gguf", &first),
		companionEntry("community/Gemma4-mmproj", "mmproj-Q8.gguf", &second),
		companionEntry("other/Proj-GGUF", "other-mmproj.gguf", &mismatched),
		companionEntry("pending/Proj-GGUF", "unreconciled.gguf", nil),
	}}

	options, err := CompanionOptions(resolver, library, preset, model.RoleProjector)
	if err != nil {
		t.Fatal(err)
	}
	if len(options) != 2 {
		t.Fatalf("options=%+v", options)
	}
	if options[0].Filename != "mmproj-F16.gguf" || options[0].Repository != "google/Gemma4-GGUF" {
		t.Fatalf("first option=%+v", options[0])
	}
	if options[1].Filename != "mmproj-Q8.gguf" || options[1].Repository != "community/Gemma4-mmproj" {
		t.Fatalf("second option=%+v", options[1])
	}
	if options[0].Classification.ProjectionDim != 5120 {
		t.Fatalf("classification=%+v", options[0].Classification)
	}
}

func TestCompanionOptionsExcludesThePrincipalItself(t *testing.T) {
	resolver, preset := companionFixture(t)
	// A draft criterion admits principal-typed artifacts, so the principal
	// would otherwise be reported as its own companion.
	principal := companionPrincipalClassification()
	twin := companionPrincipalClassification()
	library := companionLibrary{entries: []model.LibraryEntry{
		companionEntry("google/Gemma4-GGUF", "gemma4-Q4_K_M.gguf", &principal),
		companionEntry("google/Gemma4-tiny-GGUF", "gemma4-tiny-Q4_K_M.gguf", &twin),
	}}
	options, err := CompanionOptions(resolver, library, preset, model.RoleDraft)
	if err != nil {
		t.Fatal(err)
	}
	if len(options) != 1 || options[0].Filename != "gemma4-tiny-Q4_K_M.gguf" {
		t.Fatalf("options=%+v", options)
	}
}

func TestPresetCompanionLookup(t *testing.T) {
	preset := Preset{Name: "vision", Companions: []CompanionAssignment{{Role: model.RoleProjector}}}
	if _, ok := preset.Companion(model.RoleProjector); !ok {
		t.Fatal("expected the projector assignment")
	}
	if _, ok := preset.Companion(model.RoleDraft); ok {
		t.Fatal("did not expect a draft assignment")
	}
}
