package model

import (
	"fmt"
	"reflect"
)

// Inspection is the internal projection of a GGUF header used by discovery and
// download publication. It reports the
// artifact's identity, its classification digest and the scalar metadata keys
// only: the vocabulary arrays are never carried, because they are 99.9% of a
// real header and nothing downstream consumes their contents. The tokenizer's
// identity, size, special token IDs and vocabulary digest survive through
// Classification, and any value not projected here remains available by reading
// the .gguf artifact again on demand.
type Inspection struct {
	SchemaVersion  int            `json:"schemaVersion"`
	Source         Source         `json:"source"`
	Artifact       Artifact       `json:"artifact"`
	Classification Classification `json:"classification"`
	Metadata       map[string]any `json:"metadata"`
}

// NewInspection derives an inspection from a decoded GGUF metadata map. The map
// itself is never retained: every projected value is copied out by key.
func NewInspection(source Source, artifact Artifact, meta map[string]any) Inspection {
	return Inspection{
		SchemaVersion:  RecordSchemaVersion,
		Source:         source,
		Artifact:       artifact,
		Classification: Classify(meta),
		Metadata:       scalarMetadata(meta),
	}
}

// Record renders the inspection as the sidecar document to persist. Publication
// needs the same identity and classification the projection already carries, so
// the download completion path builds its record from the inspection rather
// than reading the header a second time.
func (i Inspection) Record() Record {
	r := Record{SchemaVersion: i.SchemaVersion, Source: i.Source, Artifact: i.Artifact}
	r.SetDerivation(Derivation{Version: DerivationVersion, Classification: i.Classification, CountParameters: i.Classification.CountParameters})
	return r
}

// scalarMetadata keeps every key whose value is not an array or slice. The
// length threshold the previous projection used was a way to hide the
// vocabulary; dropping arrays outright removes the data instead of hiding it.
func scalarMetadata(meta map[string]any) map[string]any {
	out := make(map[string]any, len(meta))
	for key, value := range meta {
		rv := reflect.ValueOf(value)
		if rv.IsValid() && (rv.Kind() == reflect.Array || rv.Kind() == reflect.Slice) {
			continue
		}
		out[key] = value
	}
	return out
}

func (i Inspection) Summary() string {
	return fmt.Sprintf("%s %s (%d scalar metadata keys)", i.Classification.Type, i.Classification.Architecture, len(i.Metadata))
}
