package presets

import (
	"errors"
	"fmt"
	"time"

	"github.com/mstranich/coquena/internal/model"
)

// InstalledLibrary enumerates the artifacts the configured models directory
// holds. *model.Manager satisfies this through Library. It is separate from
// ModelResolver because assigning a companion needs one artifact resolved by
// reference, while reporting the options needs every installed artifact.
type InstalledLibrary interface {
	Library() ([]model.LibraryEntry, error)
}

// CompanionAssignment is a preset's advisory assignment of a companion role to
// a model reference, alongside the preset's principal binding.
//
// It follows the requested/resolved layering used by ModelBinding: Requested is
// durable and is never rewritten, Resolved is the advisory attestation of what
// it matched when the assignment was made.
//
// The criterion itself is deliberately NOT persisted. It is a function of the
// preset's principal — the projector's projection dimension against the
// principal's embedding length, the draft's tokenizer family against the
// target's — so a stored copy would go stale the moment the principal binding
// changed. It is recomputed from the principal on every read through
// CompanionCriterion, which is what makes the assignment a criterion rather
// than a pin: several installed artifacts can satisfy it at once, and replacing
// the file the assignment resolved to does not orphan it.
type CompanionAssignment struct {
	Role      model.CompanionRole `json:"role"`
	Requested ModelReference      `json:"requested"`
	Resolved  *ResolvedModel      `json:"resolved,omitempty"`
	// UnmetCriterion records the criterion an operator deliberately overrode.
	// It is empty for an assignment that satisfied its criterion, and reads
	// report it as a diagnostic when it is set.
	UnmetCriterion string `json:"unmetCriterion,omitempty"`
}

// Diagnostic reports the unmet criterion an overridden assignment carries.
func (a CompanionAssignment) Diagnostic() (string, bool) {
	return a.UnmetCriterion, a.UnmetCriterion != ""
}

// CompanionOption is one installed artifact admissible for a preset's companion
// criterion. Options are reported, never chosen: compatible companions are
// interchangeable within their compatibility class, so the choice is the
// operator's.
type CompanionOption struct {
	Repository         string               `json:"repository"`
	CanonicalReference string               `json:"canonicalReference"`
	Filename           string               `json:"filename"`
	Classification     model.Classification `json:"classification"`
}

// ErrNoPrincipal reports that a preset cannot carry a companion criterion
// because it has no usable principal to evaluate one against.
//
// This is refused even under an override. An override passes an assignment that
// FAILS a criterion; it cannot conjure a criterion that does not exist.
var ErrNoPrincipal = errors.New("preset has no classified bound model to evaluate a companion criterion against")

// ErrCompanionCriterion refuses an assignment that does not satisfy its role's
// compatibility criterion, naming the criterion that failed.
type ErrCompanionCriterion struct {
	Role      model.CompanionRole
	Reference string
	Criterion string
	Outcome   model.Compatibility
}

func (e *ErrCompanionCriterion) Error() string {
	return fmt.Sprintf("companion %q is %s as a %s: it does not satisfy the criterion %q", e.Reference, e.Outcome, e.Role, e.Criterion)
}

// ValidCompanionRole reports whether role is one a preset may assign.
func ValidCompanionRole(role model.CompanionRole) bool {
	return role == model.RoleDraft || role == model.RoleProjector
}

// CriterionName names the compatibility criterion a role imposes, so a refusal
// can say which requirement was not met rather than only that one was not.
func CriterionName(role model.CompanionRole) string {
	switch role {
	case model.RoleProjector:
		return "projection dimension matches the principal's embedding length"
	case model.RoleDraft:
		return "tokenizer family matches the target's tokenizer family"
	default:
		return string(role)
	}
}

// Companion returns the preset's assignment for role.
func (p Preset) Companion(role model.CompanionRole) (CompanionAssignment, bool) {
	for _, a := range p.Companions {
		if a.Role == role {
			return a, true
		}
	}
	return CompanionAssignment{}, false
}

// Principal resolves the preset's bound model to its installed, classified
// entry. Every companion operation goes through it, because a criterion is
// always stated against the principal.
func Principal(resolver ModelResolver, preset Preset) (model.LibraryEntry, error) {
	if preset.Model == nil {
		return model.LibraryEntry{}, fmt.Errorf("preset %q: %w; bind a model first", preset.Name, ErrNoPrincipal)
	}
	reference := preset.Model.Requested.Raw
	if preset.Model.Resolved != nil {
		reference = ResolvedReference(*preset.Model)
	}
	entry, err := resolver.ResolveInstalled(reference)
	if err != nil {
		return model.LibraryEntry{}, fmt.Errorf("preset %q: %w: %w", preset.Name, ErrNoPrincipal, err)
	}
	if entry.Classification == nil {
		return model.LibraryEntry{}, fmt.Errorf("preset %q: %w; reconcile %q first", preset.Name, ErrNoPrincipal, entry.Source.Filename)
	}
	return entry, nil
}

// CompanionCriterion derives the criterion a companion must satisfy for the
// preset's principal. It is computed on demand rather than stored, so it always
// reflects the model the preset is bound to now.
func CompanionCriterion(resolver ModelResolver, preset Preset, role model.CompanionRole) (model.Criterion, error) {
	if !ValidCompanionRole(role) {
		return model.Criterion{}, fmt.Errorf("companion role %q is not one of %q or %q", role, model.RoleDraft, model.RoleProjector)
	}
	entry, err := Principal(resolver, preset)
	if err != nil {
		return model.Criterion{}, err
	}
	if role == model.RoleProjector {
		return model.ProjectorCriterion(*entry.Classification), nil
	}
	return model.DraftCriterion(*entry.Classification), nil
}

// BindCompanion evaluates reference against the preset's criterion for role and
// returns the assignment to store.
//
// An assignment failing the criterion is refused with a diagnostic naming the
// criterion, unless override is set, in which case it is stored carrying that
// diagnostic. Unknown is refused alongside not-admissible: an assignment whose
// criterion cannot be decided has not satisfied it either. Override covers the
// criterion only — an unresolvable reference is still an error, since an
// artifact that is not installed cannot be evaluated at all.
func BindCompanion(resolver ModelResolver, preset Preset, role model.CompanionRole, reference string, override bool) (CompanionAssignment, error) {
	criterion, err := CompanionCriterion(resolver, preset, role)
	if err != nil {
		return CompanionAssignment{}, err
	}
	entry, err := resolver.ResolveInstalled(reference)
	if err != nil {
		return CompanionAssignment{}, err
	}
	if entry.Classification == nil {
		return CompanionAssignment{}, fmt.Errorf("companion %q is not reconciled, so its classification is unknown", reference)
	}
	requested, err := parseModelReference(reference)
	if err != nil {
		return CompanionAssignment{}, err
	}
	assignment := CompanionAssignment{
		Role:      role,
		Requested: requested,
		Resolved: &ResolvedModel{
			CanonicalReference: model.CanonicalReference(entry.Source),
			Filename:           entry.Source.Filename,
			ResolvedRevision:   entry.Source.ResolvedRevision,
			ResolvedAt:         time.Now().UTC().Format(time.RFC3339),
		},
	}
	outcome := criterion.Evaluate(*entry.Classification)
	if outcome == model.Admissible {
		return assignment, nil
	}
	refusal := &ErrCompanionCriterion{Role: role, Reference: reference, Criterion: CriterionName(role), Outcome: outcome}
	if !override {
		return CompanionAssignment{}, fmt.Errorf("%w; pass --allow-incompatible to assign it anyway", refusal)
	}
	assignment.UnmetCriterion = refusal.Error()
	return assignment, nil
}

// CompanionOptions reports every installed artifact satisfying the preset's
// criterion for role, in library order and selecting none. The principal itself
// is excluded: a model is never its own companion.
func CompanionOptions(resolver ModelResolver, library InstalledLibrary, preset Preset, role model.CompanionRole) ([]CompanionOption, error) {
	criterion, err := CompanionCriterion(resolver, preset, role)
	if err != nil {
		return nil, err
	}
	principal, err := Principal(resolver, preset)
	if err != nil {
		return nil, err
	}
	entries, err := library.Library()
	if err != nil {
		return nil, err
	}
	byName := map[string]model.LibraryEntry{}
	candidates := make([]model.Companion, 0, len(entries))
	for _, entry := range entries {
		if entry.State != "installed" || entry.Classification == nil {
			continue
		}
		if entry.TargetPath == principal.TargetPath {
			continue
		}
		byName[entry.TargetPath] = entry
		candidates = append(candidates, model.Companion{Name: entry.TargetPath, Classification: *entry.Classification})
	}
	admissible := model.EvaluateCompanions(criterion, candidates)
	options := make([]CompanionOption, 0, len(admissible))
	for _, candidate := range admissible {
		entry := byName[candidate.Name]
		options = append(options, CompanionOption{
			Repository:         entry.Source.Repository,
			CanonicalReference: model.CanonicalReference(entry.Source),
			Filename:           entry.Source.Filename,
			Classification:     candidate.Classification,
		})
	}
	return options, nil
}
