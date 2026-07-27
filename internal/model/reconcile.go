package model

import (
	"fmt"
	"os"
	"path/filepath"
)

// ReconcileResult reports the outcome of a successful reconciliation.
type ReconcileResult struct {
	Record Record
	// Partial is true when --online succeeded without a content hash to
	// compare against (Hugging Face reported no LFS oid for the matched
	// sibling, e.g. a small non-LFS-tracked file). It is always false for
	// ReconcileAccept, which never contacts Hugging Face.
	Partial bool
}

// ReconcileOnline reconciles the .gguf at path against Hugging Face, treating
// its path-derived repository (the two path segments above the file,
// mirroring unreconciledSource) as the candidate identity. It resolves that
// repository's "main" revision, locates the sibling whose filename matches
// path's base name exactly, hashes the local file, and compares the digest
// against the LFS content hash (oid) Hugging Face reports for that sibling,
// when one is available. On agreement — or when no oid is available to
// compare, which is not treated as a mismatch — it persists the same
// .json/.revision sidecar pair a normal download produces, with
// Artifact.SHA256 populated from the just-computed digest. On disagreement it
// refuses and writes no sidecar.
func (m *Manager) ReconcileOnline(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := unreconciledSource(m.ModelsDir, path)
	if source.Repository == "" || source.Filename == "" {
		return ReconcileResult{}, fmt.Errorf("cannot derive a Hugging Face repository and filename from path %q", path)
	}
	if err := m.checkReconcilePath(source, path); err != nil {
		return ReconcileResult{}, err
	}
	candidates, err := m.Resolver.ResolveRevision(source.Repository, "main", source.Filename)
	if err != nil {
		return ReconcileResult{}, err
	}
	if len(candidates) != 1 {
		return ReconcileResult{}, fmt.Errorf("reconcile: expected exactly one Hugging Face artifact matching %q in %s, got %d", source.Filename, source.Repository, len(candidates))
	}
	candidate := candidates[0]
	digest, err := hashFile(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	partial := candidate.LFSOID == ""
	if !partial && candidate.LFSOID != digest {
		return ReconcileResult{}, fmt.Errorf("reconcile refused: local file digest %s disagrees with Hugging Face content hash %s for %s", digest, candidate.LFSOID, source.Filename)
	}
	record := Record{SchemaVersion: RecordSchemaVersion, Source: candidate.Source, Artifact: candidate.Artifact}
	record.Artifact.SHA256 = digest
	if _, err = PublishInPlace(m.ModelsDir, record); err != nil {
		return ReconcileResult{}, err
	}
	m.logf("model reconcile online completed path=%s repository=%s filename=%s partial=%v", path, source.Repository, source.Filename, partial)
	return ReconcileResult{Record: record, Partial: partial}, nil
}

// ReconcileAccept reconciles the .gguf at path with no network access,
// deriving Repository/Filename from the path the same way ReconcileOnline
// does, hashing the local file, and persisting a reduced "local"-provider
// sidecar so a later verify has a baseline. Source.Validate accepts
// "local" with only Repository and Filename required.
func (m *Manager) ReconcileAccept(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := unreconciledSource(m.ModelsDir, path)
	source.Provider = "local"
	if source.Repository == "" || source.Filename == "" {
		return ReconcileResult{}, fmt.Errorf("cannot derive a repository and filename from path %q", path)
	}
	if err := m.checkReconcilePath(source, path); err != nil {
		return ReconcileResult{}, err
	}
	info, err := os.Stat(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	digest, err := hashFile(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	record := Record{SchemaVersion: RecordSchemaVersion, Source: source, Artifact: Artifact{SizeBytes: info.Size(), SHA256: digest}}
	if _, err = PublishInPlace(m.ModelsDir, record); err != nil {
		return ReconcileResult{}, err
	}
	m.logf("model reconcile accept completed path=%s repository=%s filename=%s", path, source.Repository, source.Filename)
	return ReconcileResult{Record: record}, nil
}

// checkReconcilePath confirms path is exactly the on-disk location that
// Repository/Filename derive to, so reconcile never operates on, or writes
// sidecars for, a file outside modelsDir or at an unexpected location.
func (m *Manager) checkReconcilePath(source Source, path string) error {
	target, err := PathFor(m.ModelsDir, source.Repository, source.Filename)
	if err != nil {
		return err
	}
	if filepath.Clean(target) != path {
		return fmt.Errorf("path %q is not at its expected library location %q", path, target)
	}
	return nil
}
