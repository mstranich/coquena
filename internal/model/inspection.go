package model

import (
	"fmt"
	"reflect"
	"sort"
)

type Inspection struct {
	SchemaVersion   int            `json:"schemaVersion"`
	Source          Source         `json:"source"`
	Artifact        Artifact       `json:"artifact"`
	GGUF            map[string]any `json:"gguf"`
	OmittedMetadata []string       `json:"omittedMetadata,omitempty"`
}

func InspectionResult(record Record, includeRaw bool) Inspection {
	result := Inspection{SchemaVersion: record.SchemaVersion, Source: record.Source, Artifact: record.Artifact, GGUF: record.GGUF}
	if includeRaw {
		return result
	}
	result.GGUF = make(map[string]any, len(record.GGUF))
	for key, value := range record.GGUF {
		rv := reflect.ValueOf(value)
		if rv.IsValid() && (rv.Kind() == reflect.Array || rv.Kind() == reflect.Slice) && rv.Len() > 256 {
			result.GGUF[key] = map[string]any{"kind": "array", "count": rv.Len(), "omitted": true}
			result.OmittedMetadata = append(result.OmittedMetadata, key)
			continue
		}
		result.GGUF[key] = value
	}
	sort.Strings(result.OmittedMetadata)
	return result
}

func (i Inspection) Summary() string {
	return fmt.Sprintf("%d metadata keys (%d large values summarized)", len(i.GGUF), len(i.OmittedMetadata))
}
