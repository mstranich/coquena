package model

import (
	"encoding/json"
	"fmt"
	"path/filepath"
	"strings"
)

// VerifyOutcome distinguishes the three possible results of verifying an
// installed model's local integrity against its sidecar's recorded digest.
type VerifyOutcome string

const (
	// VerifyMatched indicates the computed digest agrees with the sidecar's
	// recorded Artifact.SHA256.
	VerifyMatched VerifyOutcome = "matched"
	// VerifyMismatched indicates the computed digest disagrees with the
	// sidecar's recorded Artifact.SHA256; the model should be considered
	// unusable until it is re-downloaded or reconciled again.
	VerifyMismatched VerifyOutcome = "mismatched"
	// VerifyNoBaseline indicates the sidecar has no recorded Artifact.SHA256
	// to compare against (an artifact installed, or reconciled, before
	// content hashing existed).
	VerifyNoBaseline VerifyOutcome = "no-baseline"
)

// VerifyResult reports the outcome of verifying an installed model's local
// file integrity against its sidecar's persisted content hash.
type VerifyResult struct {
	Outcome              VerifyOutcome         `json:"outcome"`
	Repository           string                `json:"repository"`
	Filename             string                `json:"filename"`
	TargetPath           string                `json:"targetPath"`
	RecordedSHA256       string                `json:"recordedSHA256,omitempty"`
	ComputedSHA256       string                `json:"computedSHA256"`
	DeclaredRecipe       string                `json:"declaredRecipe,omitempty"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
	DerivationStale      bool                  `json:"derivationStale"`
	Repaired             bool                  `json:"repaired,omitempty"`
}

type VerifyOptions struct{ RepairDerived bool }

// Verify resolves reference against the installed model library, re-hashes
// the resolved artifact's file on disk, and compares it against the
// sidecar's persisted Artifact.SHA256. It distinguishes three outcomes: a
// hash match (VerifyMatched), a hash mismatch (VerifyMismatched, meaning the
// model should be treated as unusable until re-downloaded or reconciled
// again), and the absence of any recorded baseline digest (VerifyNoBaseline,
// for artifacts installed or reconciled before content hashing existed).
// It refuses with whatever error ResolveInstalled reports when reference
// does not resolve to exactly one installed artifact.
func (m *Manager) Verify(reference string) (VerifyResult, error) {
	return m.VerifyWithOptions(reference, VerifyOptions{})
}

func (m *Manager) VerifyWithOptions(reference string, options VerifyOptions) (VerifyResult, error) {
	entry, err := m.ResolveInstalled(reference)
	if err != nil {
		return VerifyResult{}, err
	}
	digest, err := hashFile(entry.TargetPath)
	if err != nil {
		return VerifyResult{}, err
	}
	record, err := ReadRecord(entry.TargetPath)
	if err != nil {
		return VerifyResult{}, err
	}
	recorded := record.Artifact.SHA256
	result := VerifyResult{
		Repository:     entry.Source.Repository,
		Filename:       entry.Source.Filename,
		TargetPath:     entry.TargetPath,
		RecordedSHA256: recorded,
		ComputedSHA256: digest,
	}
	if record.Classification.FileType != nil {
		result.DeclaredRecipe = LlamaFileTypeDisplayName(*record.Classification.FileType)
	}
	result.PhysicalQuantization = record.Derivation.PhysicalQuantization
	result.DerivationStale = record.DerivationStale()
	switch {
	case recorded == "":
		result.Outcome = VerifyNoBaseline
	case recorded == digest:
		result.Outcome = VerifyMatched
	default:
		result.Outcome = VerifyMismatched
	}
	if options.RepairDerived {
		if result.Outcome != VerifyMatched {
			return result, fmt.Errorf("cannot repair derived evidence unless integrity matches the recorded baseline")
		}
		derivation := deriveFile(entry.TargetPath)
		record.SchemaVersion = RecordSchemaVersion
		record.SetDerivation(derivation)
		data, marshalErr := json.MarshalIndent(record, "", "  ")
		if marshalErr != nil {
			return result, marshalErr
		}
		data = append(data, '\n')
		base := strings.TrimSuffix(entry.TargetPath, filepath.Ext(entry.TargetPath))
		if err = writeAtomic(base+".json", data); err != nil {
			return result, err
		}
		result.DeclaredRecipe = ""
		if derivation.Classification.FileType != nil {
			result.DeclaredRecipe = LlamaFileTypeDisplayName(*derivation.Classification.FileType)
		}
		result.PhysicalQuantization = derivation.PhysicalQuantization
		result.DerivationStale = false
		result.Repaired = true
	}
	return result, nil
}
