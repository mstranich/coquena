package fields

import (
	"regexp"
	"strconv"
)

// Status is the three-valued result of evaluating a field's availability.
type Status string

const (
	Admitted    Status = "admitted"
	NotAdmitted Status = "not-admitted"
	Unknown     Status = "unknown"
)

var tagRE = regexp.MustCompile(`^b(\d+)$`)

func validTag(tag string) bool { return tagRE.MatchString(tag) }

// ValidReleaseTag reports whether tag is a well-formed official b<digits>
// release tag.
func ValidReleaseTag(tag string) bool { return validTag(tag) }

// CompareReleaseTags orders two official b<digits> release tags. ok is false
// when either tag cannot be parsed.
func CompareReleaseTags(a, b string) (result int, ok bool) { return compareTags(a, b) }

// compareTags orders two official b<digits> release tags. ok is false when
// either tag cannot be parsed.
func compareTags(a, b string) (result int, ok bool) {
	ma := tagRE.FindStringSubmatch(a)
	mb := tagRE.FindStringSubmatch(b)
	if ma == nil || mb == nil {
		return 0, false
	}
	ai, errA := strconv.ParseUint(ma[1], 10, 64)
	bi, errB := strconv.ParseUint(mb[1], 10, 64)
	if errA != nil || errB != nil {
		return 0, false
	}
	switch {
	case ai < bi:
		return -1, true
	case ai > bi:
		return 1, true
	default:
		return 0, true
	}
}

// Evaluate reports whether a field is admitted at the given official release.
// It never blocks authoring or mutation; it is advisory only.
func Evaluate(f Field, provider Provider, release string) Status {
	if f.Availability == nil || f.Availability.Provider != provider {
		return Unknown
	}
	if provider != ProviderOfficial {
		return Unknown
	}
	if !validTag(release) {
		return Unknown
	}
	if f.Availability.IntroducedIn == "" {
		return Unknown
	}
	if cmp, ok := compareTags(release, f.Availability.IntroducedIn); !ok || cmp < 0 {
		if !ok {
			return Unknown
		}
		return NotAdmitted
	}
	if f.Availability.RemovedIn != "" {
		if cmp, ok := compareTags(release, f.Availability.RemovedIn); !ok {
			return Unknown
		} else if cmp >= 0 {
			return NotAdmitted
		}
	}
	return Admitted
}
