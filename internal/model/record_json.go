package model

import "encoding/json"

// MarshalJSON writes the record exactly as it stands. It does not stamp a
// current derivation version onto a record whose derivation is older: a write
// that had no licence to re-derive the artifact must not claim the current
// algorithm produced what an earlier one did. Repair is a deliberate act, and
// SetDerivation is how it is performed.
func (r Record) MarshalJSON() ([]byte, error) {
	known := map[string]any{
		"schemaVersion": r.SchemaVersion,
		"source":        r.Source,
		"artifact":      r.Artifact,
		"derivation":    r.Derivation,
	}
	// Each axis is written only when it has something to say. An absent key is
	// the evidence that nothing was recorded on that axis.
	if r.Verification != nil {
		known["verification"] = r.Verification
	}
	if r.Observation != nil {
		known["observation"] = r.Observation
	}
	if r.Attestation != nil {
		known["attestation"] = r.Attestation
	}
	if r.Ownership != nil {
		known["ownership"] = r.Ownership
	}
	for key, value := range r.Extra {
		if _, reserved := known[key]; !reserved {
			known[key] = value
		}
	}
	return json.Marshal(known)
}

// UnmarshalJSON reads the current schema and no other. There is no
// preceding-version branch: admitting an earlier shape would mean inventing the
// axes it does not carry, and inventing evidence is what this record exists to
// prevent. ReadRecord rejects a foreign version before validation, so a reader
// that gets here is reading the current shape or a malformed one.
func (r *Record) UnmarshalJSON(data []byte) error {
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	if err := json.Unmarshal(raw["schemaVersion"], &r.SchemaVersion); err != nil {
		return err
	}
	if err := json.Unmarshal(raw["source"], &r.Source); err != nil {
		return err
	}
	if err := json.Unmarshal(raw["artifact"], &r.Artifact); err != nil {
		return err
	}
	if r.SchemaVersion == RecordSchemaVersion {
		if err := json.Unmarshal(raw["derivation"], &r.Derivation); err != nil {
			return err
		}
		r.Classification = r.Derivation.Classification
		r.Classification.CountParameters = r.Derivation.CountParameters
		for key, target := range map[string]any{
			"verification": &r.Verification,
			"observation":  &r.Observation,
			"attestation":  &r.Attestation,
			"ownership":    &r.Ownership,
		} {
			if value, present := raw[key]; present {
				if err := json.Unmarshal(value, target); err != nil {
					return err
				}
			}
		}
		r.Local = r.localAxis()
	}
	delete(raw, "schemaVersion")
	delete(raw, "source")
	delete(raw, "artifact")
	delete(raw, "derivation")
	delete(raw, "verification")
	delete(raw, "observation")
	delete(raw, "attestation")
	delete(raw, "ownership")
	// Extra survives a rewrite so a hand-written annotation is not discarded by
	// an operation that had no reason to touch it. It is not a version bridge.
	r.Extra = raw
	return nil
}

// localAxis projects the local evidence axis over the integrity digest, which
// is where the fact actually lives.
func (r Record) localAxis() Local {
	digest, ok := r.Artifact.Integrity()
	if !ok {
		return Local{}
	}
	return Local{Digest: digest, ComputedAt: digest.ComputedAt}
}

func (r *Record) SetDerivation(d Derivation) {
	r.Derivation = d
	r.Classification = d.Classification
	r.Classification.CountParameters = d.CountParameters
}

// SetIntegrity records the digest Coquena computed from the local file and
// refreshes the local axis projection over it.
func (r *Record) SetIntegrity(value, computedAt string) {
	r.Artifact.SetDigest(LocalDigest(value, computedAt))
	r.Local = r.localAxis()
}

func (r Record) DerivationStale() bool { return r.Derivation.Version != DerivationVersion }
