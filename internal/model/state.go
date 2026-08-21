package model

// ArtifactState is what Coquena reports about an artifact it enumerated.
//
// The seven values are settled in docs/decisions/model-artifact-states.md, and
// the derivation below is the diagram beside it. A state is read from the
// evidence at display time and is never persisted: what a sidecar stores is
// evidence, and the state is one reading of it.
type ArtifactState string

const (
	// StateUntracked is an artifact present on disk and outside Coquena's
	// management. It has no sidecar, so nothing else about it is known.
	StateUntracked ArtifactState = "untracked"
	// StateAltered is an artifact whose recorded verification found its bytes
	// disagreeing with the digest recorded for them.
	StateAltered ArtifactState = "altered"
	// StateIncomplete is an artifact whose principal is not fully constituted.
	// Nothing reaches it yet: see stateOf.
	StateIncomplete ArtifactState = "incomplete"
	// StateLocal is an artifact adopted deliberately, with no repository ever
	// consulted for it. A privately obtained model rests here permanently.
	StateLocal ArtifactState = "local"
	// StateDetached is an artifact for which a repository was consulted and no
	// match was established. It names the situation, never the cause.
	StateDetached ArtifactState = "detached"
	// StateSynced is an artifact bound to the repository revision Coquena last
	// knows the repository to carry.
	StateSynced ArtifactState = "synced"
	// StateUpgradable is an artifact bound to a revision earlier than the one
	// last observed at the repository.
	StateUpgradable ArtifactState = "upgradable"
)

// usableStates are the states from which an artifact may be referenced by a
// preset. The three absent from this set are unusable for concrete reasons: a
// preset cannot bind an artifact Coquena does not manage, one whose bytes
// changed underneath it, or one missing pieces it needs to load.
var usableStates = map[ArtifactState]bool{
	StateLocal:      true,
	StateDetached:   true,
	StateSynced:     true,
	StateUpgradable: true,
}

// Usable reports whether an artifact in this state may be referenced by a
// preset. It is the whole of what usability means.
func (s ArtifactState) Usable() bool { return usableStates[s] }

// stateOf derives an artifact's state from the evidence recorded about it. A nil
// record means no sidecar could be read.
//
// The order of the gates is normative, not stylistic. Several of these
// conditions hold at once on a real artifact — one whose bytes were found
// altered may also carry a perfectly good attestation — so what the operator is
// told depends entirely on which question is asked first. Deriving each state
// independently and asserting exactly one holds would be a runtime invariant
// that is false by construction.
//
// It reads no bytes. Every answer comes from the record, which is why `verify`
// has to persist its outcome: re-deriving `altered` here would mean hashing
// every artifact in the library on every listing.
func stateOf(record *Record) ArtifactState {
	// 1. Management. An artifact with no sidecar is outside Coquena's
	// management, and nothing further can be asked about it.
	if record == nil {
		return StateUntracked
	}
	// 2. Local integrity. Tested before anything about the repository, because
	// bytes that no longer match what was recorded make every claim resting on
	// them — including a valid attestation — a claim about bytes that are gone.
	if record.Altered() {
		return StateAltered
	}
	// 3. Composition.
	if principalIsIncomplete(record) {
		return StateIncomplete
	}
	// 4. Attestation. Standing already collapses the axes into what is known
	// about where the bytes came from.
	switch record.Standing() {
	case StandingUnattested:
		return StateLocal
	case StandingPartial:
		return StateDetached
	}
	// 5. Match. An attestation applies, so the question left is whether the
	// revision it binds is still the one the repository carries. With no later
	// observation, the attestation's own revision is the most recent thing
	// Coquena learned from the repository, and the artifact matches it.
	attestation, _ := record.AppliedAttestation()
	if record.Observation != nil && record.Observation.ResolvedCommit != "" &&
		record.Observation.ResolvedCommit != attestation.ResolvedCommit {
		return StateUpgradable
	}
	return StateSynced
}

// principalIsIncomplete reports whether an artifact's principal is missing
// pieces it needs to load.
//
// It is always false today, and deliberately so. Deciding it requires knowing
// which files constitute a quantization, and Coquena addresses files rather than
// quantizations: there is no notion of a shard anywhere in the codebase, so
// there is no evidence from which the answer could be read. The gate keeps its
// place in the cascade because its position — after altered bytes, before
// anything about the repository — is the part a later change would most easily
// get wrong. `address-quantization-bundles` replaces this body.
func principalIsIncomplete(*Record) bool { return false }
