package model

// Compatibility is the three-valued outcome of evaluating a companion
// criterion. Unknown is distinct from NotAdmissible: an artifact whose header
// never declared the linking value cannot be refused on evidence it does not
// carry, so it degrades to an explicit unknown rather than a wrong refusal.
type Compatibility string

const (
	// Admissible reports that the candidate satisfies the criterion.
	Admissible Compatibility = "admissible"
	// NotAdmissible reports that both sides declared the linking values and
	// they disagree.
	NotAdmissible Compatibility = "not-admissible"
	// CompatibilityUnknown reports that at least one linking value is missing,
	// so the criterion cannot be decided either way.
	CompatibilityUnknown Compatibility = "unknown"
)

// CompanionRole is the role a companion artifact plays for a principal model.
// Only RoleProjector is derivable from an artifact's own metadata; RoleDraft is
// assigned by the operator, since a draft model declares general.type "model"
// exactly like the principal it drafts for.
type CompanionRole string

const (
	// RoleProjector is a multimodal projector companion.
	RoleProjector CompanionRole = "projector"
	// RoleDraft is a speculative-decoding draft companion.
	RoleDraft CompanionRole = "draft"
)

// ProjectorAdmissible reports whether projector is admissible for principal.
//
// The linking key is the projector's projection dimension, never
// clip.vision.embedding_length: the latter is the vision encoder's internal
// width and matches nothing in the principal. Verified on real artifacts —
// Bonsai's projector declares projection dimension 5120, matching
// qwen35.embedding_length 5120, while its vision embedding length is 1152. The
// gemma-4 pair reads 3840 for both keys and so cannot disambiguate on its own,
// which is why the criterion is written against the projection dimension
// explicitly rather than against whichever key happens to match.
func ProjectorAdmissible(projector, principal Classification) Compatibility {
	if projector.ProjectionDim <= 0 || principal.EmbeddingLength <= 0 {
		return CompatibilityUnknown
	}
	if projector.ProjectionDim != principal.EmbeddingLength {
		return NotAdmissible
	}
	return Admissible
}

// DraftAdmissible reports whether draft is admissible as a speculative draft
// for target.
//
// Speculative decoding works because the draft proposes token IDs the target
// then verifies. Two different tokenizers give the same IDs different meanings,
// so verification would silently validate the wrong tokens. The criterion is
// therefore tokenizer family equality: identity, pre-tokenizer, vocabulary
// size, the special token IDs, and a digest of the vocabulary itself. Comparing
// digests is an exact comparison of the whole vocabulary at fixed cost.
func DraftAdmissible(draft, target Classification) Compatibility {
	if draft.Tokenizer.Model == "" || target.Tokenizer.Model == "" {
		return CompatibilityUnknown
	}
	if draft.Tokenizer.Digest == "" || target.Tokenizer.Digest == "" {
		return CompatibilityUnknown
	}
	if draft.Tokenizer.VocabSize <= 0 || target.Tokenizer.VocabSize <= 0 {
		return CompatibilityUnknown
	}
	if draft.Tokenizer.Model != target.Tokenizer.Model ||
		draft.Tokenizer.Pre != target.Tokenizer.Pre ||
		draft.Tokenizer.VocabSize != target.Tokenizer.VocabSize ||
		draft.Tokenizer.Digest != target.Tokenizer.Digest ||
		!sameSpecialToken(draft.Tokenizer.BOS, target.Tokenizer.BOS) ||
		!sameSpecialToken(draft.Tokenizer.EOS, target.Tokenizer.EOS) ||
		!sameSpecialToken(draft.Tokenizer.Pad, target.Tokenizer.Pad) ||
		!sameSpecialToken(draft.Tokenizer.Unk, target.Tokenizer.Unk) {
		return NotAdmissible
	}
	return Admissible
}

// sameSpecialToken compares two optional token IDs. They are pointers because 0
// is a legitimate token ID, so an absent ID and ID zero must stay
// distinguishable.
func sameSpecialToken(a, b *int64) bool {
	if a == nil || b == nil {
		return a == nil && b == nil
	}
	return *a == *b
}

// Companion is a candidate artifact a criterion is evaluated against. It
// carries only a name and a classification: evaluation is a pure computation
// over headers already read, and never touches the filesystem or the network.
type Companion struct {
	Name           string         `json:"name"`
	Classification Classification `json:"classification"`
}

// Criterion is the companion compatibility requirement a principal implies.
// A binding stores this criterion rather than a path to a specific file,
// because several installed artifacts can satisfy it simultaneously and a hard
// pin would be orphaned whenever the file it names is replaced.
type Criterion struct {
	Role      CompanionRole  `json:"role"`
	Principal Classification `json:"principal"`
}

// ProjectorCriterion is the criterion a projector must satisfy for principal.
func ProjectorCriterion(principal Classification) Criterion {
	return Criterion{Role: RoleProjector, Principal: principal}
}

// DraftCriterion is the criterion a draft must satisfy for target.
func DraftCriterion(target Classification) Criterion {
	return Criterion{Role: RoleDraft, Principal: target}
}

// Evaluate reports the criterion's three-valued outcome for one candidate. A
// candidate whose declared type cannot play the role is refused outright: an
// mmproj can never be a draft, and a principal model can never be a projector.
func (c Criterion) Evaluate(candidate Classification) Compatibility {
	switch c.Role {
	case RoleProjector:
		if candidate.Type == TypeModel {
			return NotAdmissible
		}
		return ProjectorAdmissible(candidate, c.Principal)
	case RoleDraft:
		if candidate.Type == TypeProjector {
			return NotAdmissible
		}
		return DraftAdmissible(candidate, c.Principal)
	default:
		return CompatibilityUnknown
	}
}

// EvaluateCompanions reports every candidate satisfying the criterion, in the
// order given. It deliberately returns all of them rather than selecting one:
// compatible companions are interchangeable within their compatibility class,
// so the choice belongs to the operator. An empty result means no installed
// artifact is admissible, never that one was chosen silently.
func EvaluateCompanions(criterion Criterion, candidates []Companion) []Companion {
	var admissible []Companion
	for _, candidate := range candidates {
		if criterion.Evaluate(candidate.Classification) == Admissible {
			admissible = append(admissible, candidate)
		}
	}
	return admissible
}
