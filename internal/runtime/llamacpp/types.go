// Package llamacpp discovers and selects official llama.cpp runtime distributions.
package llamacpp

import (
	"errors"
	"fmt"
	"slices"
	"strconv"
	"strings"
)

const SchemaVersion = 1

type Target string

const (
	TargetCPU Target = "cpu"
	TargetGPU Target = "gpu"
)

type AssetKind string

const (
	AssetPrimary    AssetKind = "primary"
	AssetDependency AssetKind = "dependency"
	AssetNonRuntime AssetKind = "non-runtime"
	AssetUnknown    AssetKind = "unknown"
)

type ReleaseAsset struct {
	Name   string    `json:"name"`
	URL    string    `json:"url"`
	Size   int64     `json:"size"`
	Digest string    `json:"digest,omitempty"`
	Kind   AssetKind `json:"kind"`
}

type Backend struct {
	Name      string `json:"name"`
	Version   string `json:"version,omitempty"`
	Flavor    string `json:"flavor,omitempty"`
	Precision string `json:"precision,omitempty"`
}

func (b Backend) String() string {
	s := b.Name
	if b.Version != "" {
		s += "@" + b.Version
	}
	if b.Flavor != "" {
		s += "/" + b.Flavor
	}
	if b.Precision != "" {
		s += "/" + b.Precision
	}
	return s
}

type Distribution struct {
	Release      string         `json:"release"`
	Target       Target         `json:"target"`
	OS           string         `json:"os"`
	Architecture string         `json:"architecture"`
	Backend      Backend        `json:"backend"`
	Primary      ReleaseAsset   `json:"primary"`
	Dependencies []ReleaseAsset `json:"dependencies"`
	Evidence     []string       `json:"evidence,omitempty"`
}

func (d Distribution) Validate() error {
	if !validTag(d.Release) {
		return fmt.Errorf("invalid release %q", d.Release)
	}
	if err := ValidateAssetID(d.Primary.Name); err != nil {
		return err
	}
	if d.Primary.Kind != AssetPrimary {
		return errors.New("primary asset has non-primary kind")
	}
	if d.OS == "" || d.Architecture == "" || d.Backend.Name == "" {
		return errors.New("distribution identity is incomplete")
	}
	if d.Target != TargetCPU && d.Target != TargetGPU {
		return fmt.Errorf("invalid target %q", d.Target)
	}
	for _, a := range d.Dependencies {
		if a.Kind != AssetDependency {
			return fmt.Errorf("dependency %q has invalid kind", a.Name)
		}
	}
	return nil
}

type Diagnostic struct {
	Code    string `json:"code"`
	Message string `json:"message"`
	Asset   string `json:"asset,omitempty"`
}

type Catalog struct {
	SchemaVersion int            `json:"schemaVersion"`
	Distributions []Distribution `json:"distributions"`
	Diagnostics   []Diagnostic   `json:"diagnostics"`
}

func (c *Catalog) Normalize() {
	if c.SchemaVersion == 0 {
		c.SchemaVersion = SchemaVersion
	}
	slices.SortFunc(c.Distributions, compareDistribution)
	slices.SortFunc(c.Diagnostics, func(a, b Diagnostic) int {
		if n := strings.Compare(a.Code, b.Code); n != 0 {
			return n
		}
		return strings.Compare(a.Asset, b.Asset)
	})
}

func compareDistribution(a, b Distribution) int {
	if n := compareTagsDesc(a.Release, b.Release); n != 0 {
		return n
	}
	if n := strings.Compare(a.OS, b.OS); n != 0 {
		return n
	}
	if n := strings.Compare(a.Architecture, b.Architecture); n != 0 {
		return n
	}
	if n := strings.Compare(a.Backend.String(), b.Backend.String()); n != 0 {
		return n
	}
	return strings.Compare(a.Primary.Name, b.Primary.Name)
}

func compareTagsDesc(a, b string) int {
	ma := tagRE.FindStringSubmatch(a)
	mb := tagRE.FindStringSubmatch(b)
	if ma != nil && mb != nil {
		ai, _ := strconv.ParseUint(ma[1], 10, 64)
		bi, _ := strconv.ParseUint(mb[1], 10, 64)
		if ai > bi {
			return -1
		}
		if ai < bi {
			return 1
		}
	}
	return strings.Compare(b, a)
}
