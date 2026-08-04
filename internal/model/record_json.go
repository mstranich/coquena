package model

import "encoding/json"

func (r Record) MarshalJSON() ([]byte, error) {
	derivation := r.Derivation
	if derivation.Version == 0 && r.SchemaVersion == RecordSchemaVersion {
		derivation = Derivation{
			Version: DerivationVersion, Classification: r.Classification,
			CountParameters: r.Classification.CountParameters,
		}
	}
	known := map[string]any{
		"schemaVersion": r.SchemaVersion,
		"source":        r.Source,
		"artifact":      r.Artifact,
		"derivation":    derivation,
	}
	for key, value := range r.Extra {
		if _, reserved := known[key]; !reserved {
			known[key] = value
		}
	}
	return json.Marshal(known)
}

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
	switch r.SchemaVersion {
	case RecordSchemaVersion:
		if err := json.Unmarshal(raw["derivation"], &r.Derivation); err != nil {
			return err
		}
		r.Classification = r.Derivation.Classification
		r.Classification.CountParameters = r.Derivation.CountParameters
	case 2:
		if err := json.Unmarshal(raw["classification"], &r.Classification); err != nil {
			return err
		}
		r.Derivation = Derivation{Classification: r.Classification, CountParameters: r.Classification.CountParameters}
	}
	delete(raw, "schemaVersion")
	delete(raw, "source")
	delete(raw, "artifact")
	delete(raw, "derivation")
	delete(raw, "classification")
	r.Extra = raw
	return nil
}

func (r *Record) SetDerivation(d Derivation) {
	r.Derivation = d
	r.Classification = d.Classification
	r.Classification.CountParameters = d.CountParameters
}

func (r Record) DerivationStale() bool { return r.Derivation.Version != DerivationVersion }
