package llamacpp

import (
	"errors"
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

var tagRE = regexp.MustCompile(`^b([1-9][0-9]*)$`)
var cudaSelectorRE = regexp.MustCompile(`^cuda(?:@([1-9][0-9]*)(?:\.([0-9]+))?)?$`)

func validTag(s string) bool { return tagRE.MatchString(s) }

type ReleaseSelector struct {
	Latest bool   `json:"latest"`
	Tag    string `json:"tag,omitempty"`
}

func ParseReleaseSelector(s string) (ReleaseSelector, error) {
	if s == "latest" || s == "" {
		return ReleaseSelector{Latest: true}, nil
	}
	if !validTag(s) {
		return ReleaseSelector{}, fmt.Errorf("invalid release %q: expected latest or b<digits>", s)
	}
	return ReleaseSelector{Tag: s}, nil
}

type BackendSelector struct {
	Name         string `json:"name"`
	VersionMajor int    `json:"versionMajor,omitempty"`
	VersionMinor *int   `json:"versionMinor,omitempty"`
}

func ParseBackendSelector(s string) (BackendSelector, error) {
	s = strings.ToLower(s)
	if s == "cpu" || s == "vulkan" {
		return BackendSelector{Name: s}, nil
	}
	m := cudaSelectorRE.FindStringSubmatch(s)
	if m == nil {
		return BackendSelector{}, fmt.Errorf("invalid backend %q: expected cpu, vulkan, cuda, or cuda@<major>[.<minor>]", s)
	}
	r := BackendSelector{Name: "cuda"}
	if m[1] != "" {
		r.VersionMajor, _ = strconv.Atoi(m[1])
	}
	if m[2] != "" {
		v, _ := strconv.Atoi(m[2])
		r.VersionMinor = &v
	}
	return r, nil
}
func (s BackendSelector) Match(b Backend) bool {
	if s.Name != b.Name {
		return false
	}
	if s.VersionMajor == 0 {
		return true
	}
	parts := strings.Split(b.Version, ".")
	if len(parts) == 0 {
		return false
	}
	major, e := strconv.Atoi(parts[0])
	if e != nil || major != s.VersionMajor {
		return false
	}
	if s.VersionMinor == nil {
		return true
	}
	if len(parts) < 2 {
		return false
	}
	minor, e := strconv.Atoi(parts[1])
	return e == nil && minor == *s.VersionMinor
}

type Selection struct {
	Release ReleaseSelector  `json:"release"`
	Backend *BackendSelector `json:"backend,omitempty"`
}

func ParseSelection(args []string) (Selection, error) {
	s := Selection{Release: ReleaseSelector{Latest: true}}
	switch len(args) {
	case 0:
		return s, nil
	case 1:
		if args[0] == "latest" || validTag(args[0]) {
			r, e := ParseReleaseSelector(args[0])
			s.Release = r
			return s, e
		}
		b, e := ParseBackendSelector(args[0])
		s.Backend = &b
		return s, e
	case 2:
		r, e := ParseReleaseSelector(args[0])
		if e != nil {
			return Selection{}, e
		}
		b, e := ParseBackendSelector(args[1])
		if e != nil {
			return Selection{}, e
		}
		s.Release = r
		s.Backend = &b
		return s, nil
	default:
		return Selection{}, errors.New("expected [latest|b<digits>] [backend]")
	}
}

type ResolutionError struct {
	Kind       string
	Candidates []string
}

func (e *ResolutionError) Error() string {
	if e.Kind == "ambiguous" {
		return fmt.Sprintf("runtime selection is ambiguous; choose one exact asset ID: %s", strings.Join(e.Candidates, ", "))
	}
	return "no compatible runtime matches the selection"
}

func Filter(ds []Distribution, selection Selection) []Distribution {
	out := make([]Distribution, 0)
	for _, d := range ds {
		if selection.Release.Tag != "" && d.Release != selection.Release.Tag {
			continue
		}
		if selection.Backend != nil && !selection.Backend.Match(d.Backend) {
			continue
		}
		out = append(out, d)
	}
	slicesSortDistributions(out)
	return out
}
func Resolve(ds []Distribution, selection Selection) (Distribution, error) {
	m := Filter(ds, selection)
	if len(m) == 0 {
		return Distribution{}, &ResolutionError{Kind: "not-found"}
	}
	if len(m) > 1 {
		ids := make([]string, len(m))
		for i := range m {
			ids[i] = m[i].Primary.Name
		}
		return Distribution{}, &ResolutionError{Kind: "ambiguous", Candidates: ids}
	}
	return m[0], nil
}
func ResolveExact(ds []Distribution, id string) (Distribution, error) {
	if err := ValidateAssetID(id); err != nil {
		return Distribution{}, err
	}
	for _, d := range ds {
		if d.Primary.Name == id {
			return d, nil
		}
	}
	return Distribution{}, &ResolutionError{Kind: "not-found"}
}
func slicesSortDistributions(ds []Distribution) {
	for i := 1; i < len(ds); i++ {
		for j := i; j > 0 && compareDistribution(ds[j], ds[j-1]) < 0; j-- {
			ds[j], ds[j-1] = ds[j-1], ds[j]
		}
	}
}
