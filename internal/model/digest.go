package model

import (
	"crypto/sha1"
	"encoding/hex"
	"fmt"
	"io"
	"os"
)

// DigestRole is what a digest is evidence *of*. The role is not derivable from
// the value or the algorithm: for an LFS-backed artifact the two roles hold the
// same value, because the local SHA-256 equals the repository's lfs.sha256, and
// it was that coincidence that let one unlabelled field survive as long as it
// did.
type DigestRole string

const (
	// RoleIntegrity is a digest Coquena computed from the local file. It
	// answers whether the bytes changed since they were recorded.
	RoleIntegrity DigestRole = "integrity"
	// RoleRepositoryMatch is a digest the repository published for the
	// artifact it claims these bytes are. It answers whether the bytes are the
	// ones the repository publishes.
	RoleRepositoryMatch DigestRole = "repository-match"
)

// DigestAlgorithm names the hash function. It is recorded rather than assumed
// because the algorithm alone does not identify a value's meaning.
type DigestAlgorithm string

const (
	AlgorithmSHA256 DigestAlgorithm = "sha256"
	AlgorithmSHA1   DigestAlgorithm = "sha1"
)

// DigestDomain names what the algorithm was applied to. It is the field that
// keeps two SHA-256 values apart: the Xet CAS hash and lfs.sha256 are both
// SHA-256 over different inputs, so (role, algorithm) cannot tell them apart
// and (role, algorithm, domain) can.
type DigestDomain string

const (
	// DomainRawFile is the algorithm applied to the file's bytes as they are.
	DomainRawFile DigestDomain = "raw-file"
	// DomainGitBlob is the algorithm applied to Git's blob framing, which
	// prefixes the bytes with a header. See GitBlobOID.
	DomainGitBlob DigestDomain = "git-blob"
	// DomainXetCAS is the Xet content-addressed store's chunked file hash.
	// Coquena records it and does not accept it: it is reproducible in
	// principle, but Coquena does not implement the chunker and gains nothing
	// by doing so, since a Git-backed repository supplies the raw SHA-256 for
	// its LFS pointer anyway.
	DomainXetCAS DigestDomain = "xet-cas"
)

// Digest is one self-describing content identifier. Every field but the value
// exists so a reader never has to infer meaning from the field a value was
// stored under — the exact failure the ETag embodied.
type Digest struct {
	Role       DigestRole      `json:"role"`
	Algorithm  DigestAlgorithm `json:"algorithm"`
	Domain     DigestDomain    `json:"domain"`
	Value      string          `json:"value"`
	ComputedAt string          `json:"computedAt,omitempty"`
}

// Reproducible reports whether Coquena can compute this digest's value from the
// complete local bytes, which is the whole test for accepting a published
// identifier as evidence.
//
// Acceptance rests on reproducibility rather than on the algorithm's name. A
// repository that publishes no content identifier does not exist — Git hashes
// everything it stores, so an identifier is always present and only its domain
// varies. Declining a domain is therefore a statement about what Coquena
// implements, never a claim that the identifier fails to describe the bytes.
func (d Digest) Reproducible() bool {
	switch {
	case d.Algorithm == AlgorithmSHA256 && d.Domain == DomainRawFile:
		return true
	case d.Algorithm == AlgorithmSHA1 && d.Domain == DomainGitBlob:
		return true
	default:
		return false
	}
}

// Tuple is the identity of a digest within an artifact. At most one digest may
// carry any given tuple; two is an invalid sidecar rather than a first-one-wins
// ambiguity.
func (d Digest) Tuple() (DigestRole, DigestAlgorithm, DigestDomain) {
	return d.Role, d.Algorithm, d.Domain
}

// LocalDigest is the integrity digest Coquena computes: SHA-256 over the raw
// file. It is the only shape RoleIntegrity ever takes, since hashFile is the
// only producer.
func LocalDigest(value, computedAt string) Digest {
	return Digest{Role: RoleIntegrity, Algorithm: AlgorithmSHA256, Domain: DomainRawFile, Value: value, ComputedAt: computedAt}
}

// PublishedDigest is a repository-supplied identifier in the domain the
// provider reported it under. It carries no ComputedAt: Coquena did not compute
// it, and the time it was *seen* belongs to the observation axis.
func PublishedDigest(algorithm DigestAlgorithm, domain DigestDomain, value string) Digest {
	return Digest{Role: RoleRepositoryMatch, Algorithm: algorithm, Domain: domain, Value: value}
}

// GitBlobOID computes the Git object ID of the file at path: the SHA-1 of
// Git's blob framing, "blob " + decimal byte length + NUL + the file's bytes.
//
// It is deliberately not sha1 of the bytes alone. A repository serves a Git
// blob OID for any sibling small enough to be stored directly in Git rather
// than in LFS, and reproducing it locally is what lets such an artifact be
// matched at all — under a SHA-256-only policy it never could be, not because
// evidence was missing but because the only evidence there is was refused.
func GitBlobOID(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	info, err := f.Stat()
	if err != nil {
		return "", err
	}
	h := sha1.New()
	if _, err = fmt.Fprintf(h, "blob %d\x00", info.Size()); err != nil {
		return "", err
	}
	if _, err = io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

// ReproduceDigest computes, from the local file, the value that would have to
// match for published to be evidence. It refuses a domain Coquena does not
// implement rather than returning something comparable-looking.
func ReproduceDigest(path string, published Digest) (string, error) {
	switch {
	case published.Algorithm == AlgorithmSHA256 && published.Domain == DomainRawFile:
		return hashFile(path)
	case published.Algorithm == AlgorithmSHA1 && published.Domain == DomainGitBlob:
		return GitBlobOID(path)
	default:
		return "", fmt.Errorf("cannot reproduce a %s digest over %s from the local file", published.Algorithm, published.Domain)
	}
}
