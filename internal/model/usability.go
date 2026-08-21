package model

// Usability is whether an artifact can take part in a runtime invocation.
//
// It carries no reason. Usability is derived wholly from the artifact's state,
// so the state is the reason: an operator reading `untracked` or `altered` has
// already been told why it cannot be bound. Keeping a parallel enumeration of
// reasons meant two places had to agree about one answer, and one of them —
// `unclassifiable` — described a case that can no longer exist, because an
// artifact Coquena cannot classify is never recorded at all.
//
// It is derived whenever the library is read and never written to a sidecar,
// because Advice depends on which other artifacts are present: an orphaned
// projector's advice changes the moment a compatible principal arrives.
type Usability struct {
	Usable bool `json:"usable"`
	// Advice is a note that does not bear on eligibility. A companion with no
	// compatible principal installed gets one: the fact is worth telling an
	// operator, and it is not a reason the artifact cannot be referenced.
	Advice string `json:"advice,omitempty"`
}

// AssessUsability derives the usability of every entry, in place, from the
// entries alone. It takes the whole library because a projector's advice is a
// property of the library rather than of the artifact: it needs to know which
// principals sit alongside it.
//
// It performs no I/O. Every entry's classification has already been read by
// enumeration, from its sidecar or from its own header, so this is a pure
// computation over what the caller already holds.
func AssessUsability(entries []LibraryEntry) {
	principals := bindablePrincipals(entries)
	for i := range entries {
		entries[i].Usability = usabilityOf(entries[i], principals)
	}
}

// bindablePrincipals collects the principal models a projector may be advised
// against. Only usable entries qualify: a principal no preset can bind cannot
// make anything beside it bindable either.
func bindablePrincipals(entries []LibraryEntry) []LibraryEntry {
	var principals []LibraryEntry
	for _, entry := range entries {
		if !entry.State.Usable() || entry.Classification == nil {
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
// The answer is the state and nothing else. A companion is eligible whatever
// principals the library holds: it cannot perform inference alone, but neither
// can any companion, and which principal it accompanies is decided by the preset
// that binds them rather than by what happens to sit beside it at the moment the
// library is read. Gating on that pre-empted the preset's decision using an
// accident of ordering. The compatibility answer is still computed and still
// useful — it is reported as advice.
func usabilityOf(entry LibraryEntry, principals []LibraryEntry) Usability {
	if !entry.State.Usable() {
		return Usability{}
	}
	result := Usability{Usable: true}
	if entry.Classification != nil && entry.Classification.Type == TypeProjector && !hasCompatiblePrincipal(*entry.Classification, principals) {
		result.Advice = "no compatible principal model is currently installed for this projector; a preset that binds it will need one"
	}
	return result
}

// hasCompatiblePrincipal reports whether any bindable principal admits this
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
