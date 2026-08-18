package model

// UnusableReason enumerates why an artifact cannot take part in a runtime
// invocation. It is an enumeration rather than a free string because the three
// cases have different remedies — reconcile the artifact, install a compatible
// principal, or accept that Coquena cannot type the file — and a caller
// rendering a listing or answering an HTTP request must be able to distinguish
// them without parsing prose.
type UnusableReason string

const (
	// ReasonIncomplete reports an artifact that has no file on disk yet,
	// because its download is still in flight. It is distinct from
	// ReasonUnclassifiable: nothing has been observed about the artifact, as
	// opposed to having been observed and found untypeable.
	ReasonIncomplete UnusableReason = "incomplete"
	// ReasonUnreconciled reports an artifact with no valid sidecar. Reading its
	// header grants it no standing, so it stays excluded from binding until an
	// operator reconciles it.
	ReasonUnreconciled UnusableReason = "unreconciled"
	// ReasonUnclassifiable reports an artifact whose declared type is absent or
	// unrecognised. Coquena refuses to infer one from architecture or filename,
	// so such an artifact is reported unusable rather than listed untyped.
	ReasonUnclassifiable UnusableReason = "unclassifiable"
	// ReasonAltered reports an artifact whose recorded verification found its
	// bytes disagreeing with the digest recorded for them. Its remedy differs
	// from every other reason here: the sidecar is valid and the type is known,
	// but the bytes are no longer the ones Coquena recorded.
	ReasonAltered UnusableReason = "altered"
)

// Usability is whether an artifact can take part in a runtime invocation, with
// the reason when it cannot.
//
// It is derived whenever the library is read and never written to a sidecar,
// because it depends on which other artifacts are present: an orphaned
// projector becomes usable the moment a compatible principal is installed, and
// a persisted answer would be stale from that instant. Detail carries the
// operator-facing sentence so a CLI listing and an HTTP response render the
// same explanation without either re-deriving it.
type Usability struct {
	Usable bool           `json:"usable"`
	Reason UnusableReason `json:"reason,omitempty"`
	Detail string         `json:"detail,omitempty"`
	// Advice is a note that does not bear on eligibility. A companion with no
	// compatible principal installed gets one: the fact is worth telling an
	// operator, and it is not a reason the artifact cannot be referenced.
	Advice string `json:"advice,omitempty"`
}

// usable is the sole usable outcome. It carries no reason, since a reason
// exists only to explain a refusal.
func usable() Usability { return Usability{Usable: true} }

// unusable pairs a reason with its operator-facing explanation.
func unusable(reason UnusableReason, detail string) Usability {
	return Usability{Usable: false, Reason: reason, Detail: detail}
}

// AssessUsability derives the usability of every entry, in place, from the
// entries alone. It takes the whole library because a projector's usability is
// a property of the library rather than of the artifact: it needs to know which
// principals are installed alongside it.
//
// It performs no I/O. Every entry's classification has already been read by
// enumeration, from its sidecar or from its own header, so this is a pure
// computation over what the caller already holds.
func AssessUsability(entries []LibraryEntry) {
	principals := installedPrincipals(entries)
	for i := range entries {
		entries[i].Usability = usabilityOf(entries[i], principals)
	}
}

// installedPrincipals collects the principal models a projector may be a
// companion of. Only installed entries qualify: an unreconciled principal is
// excluded from binding, so it cannot make anything else usable either.
func installedPrincipals(entries []LibraryEntry) []LibraryEntry {
	var principals []LibraryEntry
	for _, entry := range entries {
		if entry.State != "installed" || entry.Classification == nil {
			continue
		}
		if entry.Classification.Type == TypeModel {
			principals = append(principals, entry)
		}
	}
	return principals
}

// usabilityOf decides whether one artifact is eligible to be referenced by a
// preset, which is the only thing usability means.
//
// A companion is eligible whatever principals the library holds. It cannot
// perform inference alone, but neither can any companion, and which principal
// it accompanies is decided by the preset that binds them rather than by what
// happens to sit beside it at the moment the library is read. Gating on that
// pre-empted the preset's decision using an accident of ordering: an operator
// who imported a projector first, meaning to fetch its principal next, was told
// the artifact they had just imported was unusable. The compatibility answer is
// still computed and still useful — it is reported as advice.
//
// The remaining cases are ordered by remedy. Altered bytes are tested before
// the sidecar check because an artifact with a recorded mismatch has a
// perfectly valid sidecar; the thing that is wrong is the file.
func usabilityOf(entry LibraryEntry, principals []LibraryEntry) Usability {
	if entry.Classification == nil {
		return unusable(ReasonIncomplete, "the artifact is not yet on disk; its download has not completed")
	}
	if entry.Altered() {
		return unusable(ReasonAltered, "the last verification found the artifact's bytes disagreeing with the digest recorded for them; re-download it, or reconcile it again if the change was intended")
	}
	if entry.State != "installed" {
		return unusable(ReasonUnreconciled, "the artifact has no valid sidecar and must be reconciled before it can be used")
	}
	if entry.Classification.Type == TypeUnknown {
		return unusable(ReasonUnclassifiable, "the artifact declares no type Coquena recognises, so it cannot be used")
	}
	if entry.Classification.Type == TypeProjector && !hasCompatiblePrincipal(*entry.Classification, principals) {
		result := usable()
		result.Advice = "no installed principal model is currently compatible with this projector; a preset that binds it will need one"
		return result
	}
	return usable()
}

// hasCompatiblePrincipal reports whether any installed principal admits this
// projector as a companion. The comparison is delegated to the existing
// companion criterion rather than repeated here, so the listing and a preset
// binding can never disagree about what compatible means.
func hasCompatiblePrincipal(projector Classification, principals []LibraryEntry) bool {
	candidate := []Companion{{Classification: projector}}
	for _, principal := range principals {
		if len(EvaluateCompanions(ProjectorCriterion(*principal.Classification), candidate)) > 0 {
			return true
		}
	}
	return false
}
