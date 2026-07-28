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
	// ReasonOrphanedProjector reports a projector compatible with no installed
	// principal. A projector is never independently usable: it contributes a
	// capability to a principal and has no meaning on its own.
	ReasonOrphanedProjector UnusableReason = "orphaned-projector"
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

// usabilityOf decides one artifact's usability against the installed
// principals.
//
// The order the cases are tested in is load-bearing. An unreconciled projector
// satisfies both the unreconciled and the orphaned test, and reconciliation is
// the reason that governs: it is what the operator must do first, and until it
// is done the artifact would be unusable whatever principals were present.
//
// A principal with no installed projector stays usable, which is why no case
// below examines a principal's companions. A projector adds a capability —
// vision — to a model that already performs inference without it, so a
// text-only model is perfectly usable alone. Refusing it would report the
// overwhelmingly common library as broken.
func usabilityOf(entry LibraryEntry, principals []LibraryEntry) Usability {
	if entry.Classification == nil {
		return unusable(ReasonIncomplete, "the artifact is not yet on disk; its download has not completed")
	}
	if entry.State != "installed" {
		return unusable(ReasonUnreconciled, "the artifact has no valid sidecar and must be reconciled before it can be used")
	}
	if entry.Classification.Type == TypeUnknown {
		return unusable(ReasonUnclassifiable, "the artifact declares no type Coquena recognises, so it cannot be used")
	}
	if entry.Classification.Type == TypeProjector && !hasCompatiblePrincipal(*entry.Classification, principals) {
		return unusable(ReasonOrphanedProjector, "no installed principal model is compatible with this projector; install one it can accompany")
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
