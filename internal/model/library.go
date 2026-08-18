package model

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func TargetPath(modelsDir string, source Source) (string, error) {
	if err := source.Validate(); err != nil {
		return "", err
	}
	return PathFor(modelsDir, source.Repository, source.Filename)
}

// PathFor computes the on-disk path for a repository and filename beneath
// modelsDir, without requiring a full Source (revision, URL, provider). It
// applies the same path-safety checks as TargetPath.
func PathFor(modelsDir, repository, filename string) (string, error) {
	parts := strings.Split(repository, "/")
	for _, v := range append(parts, filename) {
		if v == "" || v == "." || v == ".." || strings.ContainsAny(v, `\\:`) {
			return "", fmt.Errorf("unsafe model path component %q", v)
		}
	}
	root := filepath.Clean(modelsDir)
	target := filepath.Join(append([]string{root}, append(parts, filename)...)...)
	rel, err := filepath.Rel(root, target)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
		return "", fmt.Errorf("model target escapes models directory")
	}
	return target, nil
}
func Publish(modelsDir, partial string, record Record) (string, error) {
	target, err := TargetPath(modelsDir, record.Source)
	if err != nil {
		return "", err
	}
	if err = os.MkdirAll(filepath.Dir(target), 0755); err != nil {
		return "", err
	}
	if _, err = os.Stat(target); err == nil {
		existing, readErr := ReadRecord(target)
		if readErr == nil && existing.Source == record.Source {
			return target, nil
		}
		return "", fmt.Errorf("model already exists with conflicting or incomplete sidecars: %s", target)
	} else if !os.IsNotExist(err) {
		return "", err
	}
	record.SchemaVersion = RecordSchemaVersion
	// The classification is derived from the completed file's own header, read
	// here while the content still sits at partial and before it is staged.
	record.SetDerivation(deriveFile(partial))
	if err = ensureIntegrity(&record, partial); err != nil {
		return "", err
	}
	record.Artifact.DownloadedAt = time.Now().UTC().Format(time.RFC3339)
	sidecar := strings.TrimSuffix(target, filepath.Ext(target))
	jsonPath := sidecar + ".json"
	revisionPath := sidecar + ".revision"
	data, err := json.MarshalIndent(record, "", "  ")
	if err != nil {
		return "", err
	}
	data = append(data, '\n')
	modelTemp, err := os.CreateTemp(filepath.Dir(target), "."+filepath.Base(target)+"-*")
	if err != nil {
		return "", err
	}
	modelTempPath := modelTemp.Name()
	defer os.Remove(modelTempPath)
	stagedByRename := filepath.Clean(filepath.Dir(partial)) == filepath.Clean(filepath.Dir(target))
	if stagedByRename {
		if err = modelTemp.Close(); err != nil {
			return "", err
		}
		if err = os.Remove(modelTempPath); err != nil {
			return "", err
		}
		if err = os.Rename(partial, modelTempPath); err != nil {
			return "", err
		}
		defer func() {
			if _, statErr := os.Stat(partial); errors.Is(statErr, os.ErrNotExist) {
				_ = os.Rename(modelTempPath, partial)
			}
		}()
	} else {
		source, openErr := os.Open(partial)
		if openErr != nil {
			_ = modelTemp.Close()
			return "", openErr
		}
		_, copyErr := io.Copy(modelTemp, source)
		closeErr := source.Close()
		if copyErr != nil || closeErr != nil || modelTemp.Sync() != nil || modelTemp.Close() != nil {
			_ = modelTemp.Close()
			if copyErr != nil {
				return "", copyErr
			}
			if closeErr != nil {
				return "", closeErr
			}
			return "", errors.New("stage model publication")
		}
	}
	jsonTemp, err := writeTemp(filepath.Dir(jsonPath), filepath.Base(jsonPath), data)
	if err != nil {
		return "", err
	}
	defer os.Remove(jsonTemp)
	revisionTemp, err := writeTemp(filepath.Dir(revisionPath), filepath.Base(revisionPath), []byte(record.Source.ResolvedRevision+"\n"))
	if err != nil {
		return "", err
	}
	defer os.Remove(revisionTemp)
	if err = os.Rename(modelTempPath, target); err != nil {
		return "", err
	}
	published := []string{target}
	if err = os.Rename(jsonTemp, jsonPath); err == nil {
		published = append(published, jsonPath)
	}
	if err == nil {
		err = os.Rename(revisionTemp, revisionPath)
		if err == nil {
			published = append(published, revisionPath)
		}
	}
	if err != nil {
		for _, path := range published {
			if stagedByRename && path == target {
				_ = os.Rename(path, partial)
			} else {
				_ = os.Remove(path)
			}
		}
		return "", err
	}
	if !stagedByRename {
		if err = os.Remove(partial); err != nil {
			return "", err
		}
	}
	return target, nil
}

// PublishInPlace writes the .json/.revision sidecar pair for a .gguf that
// already exists at its final on-disk location (the shape reconcile always
// operates on, since Library derives Repository/Filename from a path already
// inside modelsDir). Unlike Publish, it never stages, copies, or renames the
// model file itself — only the two sidecar files are written, atomically.
//
// It returns the record it actually persisted rather than the target path,
// which no caller ever used: the classification and publication timestamp are
// derived here, onto this function's own copy, so a caller returning the record
// it passed in would hand back a zero classification.
func PublishInPlace(modelsDir string, record Record) (Record, error) {
	target, err := TargetPath(modelsDir, record.Source)
	if err != nil {
		return Record{}, err
	}
	if _, err = os.Stat(target); err != nil {
		return Record{}, err
	}
	record.SchemaVersion = RecordSchemaVersion
	record.SetDerivation(deriveFile(target))
	if err = ensureIntegrity(&record, target); err != nil {
		return Record{}, err
	}
	preserveUnownedAxes(target, &record)
	record.Artifact.DownloadedAt = time.Now().UTC().Format(time.RFC3339)
	sidecar := strings.TrimSuffix(target, filepath.Ext(target))
	jsonPath := sidecar + ".json"
	revisionPath := sidecar + ".revision"
	data, err := json.MarshalIndent(record, "", "  ")
	if err != nil {
		return Record{}, err
	}
	data = append(data, '\n')
	jsonTemp, err := writeTemp(filepath.Dir(jsonPath), filepath.Base(jsonPath), data)
	if err != nil {
		return Record{}, err
	}
	defer os.Remove(jsonTemp)
	revisionTemp, err := writeTemp(filepath.Dir(revisionPath), filepath.Base(revisionPath), []byte(record.Source.ResolvedRevision+"\n"))
	if err != nil {
		return Record{}, err
	}
	defer os.Remove(revisionTemp)
	if err = os.Rename(jsonTemp, jsonPath); err != nil {
		return Record{}, err
	}
	if err = os.Rename(revisionTemp, revisionPath); err != nil {
		_ = os.Remove(jsonPath)
		return Record{}, err
	}
	return record, nil
}

// classifyFile derives the classification digest from an artifact's own GGUF
// header. A file whose header cannot be read — a foreign, truncated, or
// deliberately accepted non-GGUF artifact — classifies as unknown rather than
// failing publication, matching the explicit-unknown rule Classify applies to
// an unrecognised declared type.
func classifyFile(path string) Classification {
	return deriveFile(path).Classification
}

func deriveFile(path string) Derivation {
	now := time.Now().UTC().Format(time.RFC3339)
	r, err := OpenLocal(path)
	if err != nil {
		return Derivation{Version: DerivationVersion, ComputedAt: now, Classification: Classification{Type: TypeUnknown}}
	}
	defer r.Close()
	scan, err := ScanGGUF(r, false)
	if err != nil {
		return Derivation{Version: DerivationVersion, ComputedAt: now, Classification: Classification{Type: TypeUnknown}}
	}
	var params uint64
	for _, tensor := range scan.Tensors {
		n, countErr := tensorElements(tensor)
		if countErr != nil || n > math.MaxInt64 || params > uint64(math.MaxInt64)-n {
			params = 0
			break
		}
		params += n
	}
	c := Classify(scan.Metadata)
	c.CountParameters = int64(params)
	d := Derivation{Version: DerivationVersion, ComputedAt: now, Classification: c, CountParameters: int64(params)}
	if typ, ok, dominantErr := DominantPhysicalType(scan); dominantErr == nil && ok {
		histogram, _ := PhysicalTensorHistogram(scan)
		d.PhysicalQuantization = &PhysicalQuantization{Type: typ, Name: GGMLTypeDisplayName(typ), CountParameters: histogram[typ]}
	}
	return d
}

// preserveUnownedAxes carries forward every axis the writing command did not
// set itself.
//
// A command that republishes an artifact's sidecar builds the record it is
// responsible for, not the whole document. Without this, reconciling an
// artifact would silently discard the operator's acceptance, an established
// attestation, and whatever the last verification found — none of which the
// reconciling command has any standing to retract. Each axis is left to the
// command that owns it, and that only works if a writer cannot drop an axis by
// simply not knowing about it.
//
// An unreadable existing sidecar carries nothing forward, which is correct: an
// invalid document is not evidence.
func preserveUnownedAxes(target string, record *Record) {
	existing, err := ReadRecord(target)
	if err != nil {
		return
	}
	if record.Verification == nil {
		record.Verification = existing.Verification
	}
	if record.Observation == nil {
		record.Observation = existing.Observation
	}
	if record.Attestation == nil {
		record.Attestation = existing.Attestation
	}
	if record.Ownership == nil {
		record.Ownership = existing.Ownership
	}
}

// writeRecordInPlace persists record over the artifact's existing sidecar and
// changes nothing else about it.
//
// It is deliberately not PublishInPlace: that function re-derives the GGUF
// evidence and stamps a fresh download time, which is correct when publishing
// an artifact and wrong when a single axis was written. A command that owns one
// axis must be able to write it without disturbing what other commands own.
func writeRecordInPlace(target string, record Record) error {
	data, err := json.MarshalIndent(record, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	base := strings.TrimSuffix(target, filepath.Ext(target))
	return writeAtomic(base+".json", data)
}

// ensureIntegrity guarantees that every sidecar Coquena writes carries the
// digest needed to detect a later change to the artifact's bytes. The invariant
// lives on the write path rather than in each caller, so "managed artifact with
// no integrity digest" is a state the schema cannot reach rather than one every
// caller has to remember not to create.
//
// A caller that already hashed the file keeps its digest: the download path
// hashes the completed content once, in the same pass that finishes the
// transfer, and re-reading a multi-gigabyte artifact here to learn what it
// already knows would be the extra pass the sidecar requirement forbids.
func ensureIntegrity(record *Record, path string) error {
	if _, ok := record.Artifact.Integrity(); ok {
		return nil
	}
	digest, err := hashFile(path)
	if err != nil {
		return err
	}
	record.SetIntegrity(digest, time.Now().UTC().Format(time.RFC3339))
	return nil
}

func ReadRecord(target string) (Record, error) {
	base := strings.TrimSuffix(target, filepath.Ext(target))
	data, err := os.ReadFile(base + ".json")
	if err != nil {
		return Record{}, err
	}
	var r Record
	if err = json.Unmarshal(data, &r); err != nil {
		return Record{}, err
	}
	// Only the current schema is read. An earlier version is not translated or
	// partially admitted: its document cannot say what the evidence axes say,
	// so admitting it would mean inventing them. The artifact takes the same
	// recovery path as a GGUF with no sidecar at all, which costs a local hash
	// and one metadata request and never touches the model file.
	if r.SchemaVersion != RecordSchemaVersion {
		return Record{}, fmt.Errorf("unsupported model sidecar schemaVersion %d; only %d is read", r.SchemaVersion, RecordSchemaVersion)
	}
	if err = r.Source.Validate(); err != nil {
		return Record{}, fmt.Errorf("invalid model source sidecar: %w", err)
	}
	if err = r.Artifact.Validate(); err != nil {
		return Record{}, fmt.Errorf("invalid model sidecar: %w", err)
	}
	// Writing a sidecar requires computing an integrity digest, so one is
	// always present in a record Coquena wrote. Its absence means the document
	// is not one of ours, rather than that this artifact's integrity happens to
	// be unknown — a state the schema deliberately cannot express.
	if _, ok := r.Artifact.Integrity(); !ok {
		return Record{}, fmt.Errorf("model sidecar records no integrity digest")
	}
	revision, err := os.ReadFile(base + ".revision")
	if err != nil {
		return Record{}, err
	}
	if string(revision) != r.Source.ResolvedRevision+"\n" {
		return Record{}, ErrInconsistentRevision
	}
	return r, nil
}
func writeAtomic(path string, data []byte) error {
	tmp, err := writeTemp(filepath.Dir(path), filepath.Base(path), data)
	if err != nil {
		return err
	}
	defer os.Remove(tmp)
	return os.Rename(tmp, path)
}
func writeTemp(dir, base string, data []byte) (string, error) {
	f, err := os.CreateTemp(dir, "."+base+"-*")
	if err != nil {
		return "", err
	}
	tmp := f.Name()
	if _, err = f.Write(data); err != nil {
		_ = f.Close()
		_ = os.Remove(tmp)
		return "", err
	}
	if err = f.Sync(); err != nil {
		_ = f.Close()
		_ = os.Remove(tmp)
		return "", err
	}
	if err = f.Close(); err != nil {
		_ = os.Remove(tmp)
		return "", err
	}
	return tmp, nil
}
