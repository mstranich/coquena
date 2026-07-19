package llamacpp

import (
	"fmt"
	"regexp"
	"strings"
)

// TaxonomyVersion changes whenever interpretation of upstream filenames changes.
const TaxonomyVersion = 1

type Release struct {
	Tag         string     `json:"tag_name"`
	Draft       bool       `json:"draft"`
	Prerelease  bool       `json:"prerelease"`
	PublishedAt string     `json:"published_at"`
	Assets      []APIAsset `json:"assets"`
}
type APIAsset struct {
	Name               string `json:"name"`
	BrowserDownloadURL string `json:"browser_download_url"`
	Size               int64  `json:"size"`
	Digest             string `json:"digest"`
}

var primaryRE = regexp.MustCompile(`^llama-(b[1-9][0-9]*)-bin-(win|ubuntu|macos|android)(?:-([a-z0-9]+)(?:-([0-9]+(?:\.[0-9]+)*|fp16|fp32|adreno))?)?-(x64|arm64|s390x)\.(zip|tar\.gz)$`)
var cudartRE = regexp.MustCompile(`^cudart-llama-bin-win-cuda-([0-9]+(?:\.[0-9]+)*)-(x64)\.zip$`)

func toArch(s string) string {
	if s == "x64" {
		return "amd64"
	}
	return s
}

func ClassifyRelease(release Release) Catalog {
	c := Catalog{SchemaVersion: SchemaVersion, Distributions: []Distribution{}, Diagnostics: []Diagnostic{}}
	if release.Draft || release.Prerelease || !validTag(release.Tag) {
		c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "release-ineligible", Message: "draft, prerelease, or malformed release was ignored"})
		return c
	}
	dependencies := map[string][]ReleaseAsset{}
	for _, raw := range release.Assets {
		if m := cudartRE.FindStringSubmatch(raw.Name); m != nil {
			key := m[1] + "/" + toArch(m[2])
			dependencies[key] = append(dependencies[key], asset(raw, AssetDependency))
		}
	}
	for _, raw := range release.Assets {
		m := primaryRE.FindStringSubmatch(raw.Name)
		if m == nil {
			kind := AssetUnknown
			code := "asset-unknown"
			if isKnownNonRuntime(raw.Name) {
				kind = AssetNonRuntime
				code = "asset-non-runtime"
			}
			if cudartRE.MatchString(raw.Name) {
				continue
			}
			_ = kind
			c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: code, Asset: raw.Name, Message: "asset is not an independently downloadable runtime"})
			continue
		}
		if m[1] != release.Tag {
			c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "asset-tag-mismatch", Asset: raw.Name, Message: "asset tag differs from release tag"})
			continue
		}
		osName := map[string]string{"win": "windows", "ubuntu": "linux", "macos": "darwin", "android": "android"}[m[2]]
		backend, ok, evidence := interpretBackend(m[2], toArch(m[5]), m[3], m[4])
		if !ok {
			c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "asset-ambiguous", Asset: raw.Name, Message: "recognized filename has no unambiguous build semantics"})
			continue
		}
		if !supportedBuildSemantics(osName, toArch(m[5]), backend.Name) {
			c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "asset-ambiguous", Asset: raw.Name, Message: "filename combination is not a known official build target"})
			continue
		}
		target := TargetGPU
		if backend.Name == "cpu" {
			target = TargetCPU
		}
		d := Distribution{Release: release.Tag, Target: target, OS: osName, Architecture: toArch(m[5]), Backend: backend, Primary: asset(raw, AssetPrimary), Dependencies: []ReleaseAsset{}, Evidence: []string{fmt.Sprintf("taxonomy-v%d", TaxonomyVersion), evidence}}
		if backend.Name == "cuda" {
			matches := dependencies[backend.Version+"/"+d.Architecture]
			if len(matches) != 1 {
				c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "cuda-dependency-incomplete", Asset: raw.Name, Message: fmt.Sprintf("expected exactly one matching cudart asset, found %d", len(matches))})
				continue
			}
			d.Dependencies = matches
		}
		if err := d.Validate(); err != nil {
			c.Diagnostics = append(c.Diagnostics, Diagnostic{Code: "distribution-invalid", Asset: raw.Name, Message: err.Error()})
			continue
		}
		c.Distributions = append(c.Distributions, d)
	}
	c.Normalize()
	return c
}

func supportedBuildSemantics(osName, arch, backend string) bool {
	allowed := map[string]bool{
		"windows/amd64/cpu": true, "windows/amd64/vulkan": true, "windows/amd64/cuda": true, "windows/amd64/hip": true, "windows/amd64/sycl": true, "windows/amd64/openvino": true,
		"windows/arm64/cpu": true, "windows/arm64/opencl": true,
		"linux/amd64/cpu": true, "linux/amd64/vulkan": true, "linux/amd64/rocm": true, "linux/amd64/sycl": true, "linux/amd64/openvino": true,
		"linux/arm64/cpu": true, "linux/arm64/vulkan": true, "linux/s390x/cpu": true,
		"darwin/arm64/metal": true, "darwin/amd64/cpu": true,
		"android/arm64/cpu": true,
	}
	return allowed[osName+"/"+arch+"/"+backend]
}

func asset(a APIAsset, k AssetKind) ReleaseAsset {
	return ReleaseAsset{Name: a.Name, URL: a.BrowserDownloadURL, Size: a.Size, Digest: a.Digest, Kind: k}
}
func isKnownNonRuntime(name string) bool {
	return strings.Contains(name, "-ui.") || strings.Contains(name, "xcframework") || name == "Source code (zip)" || name == "Source code (tar.gz)"
}

func interpretBackend(platform, arch, kind, qual string) (Backend, bool, string) {
	if kind == "" {
		if platform == "macos" && arch == "arm64" {
			return Backend{Name: "metal", Flavor: "embedded"}, true, "official-macos-arm64-metal"
		}
		return Backend{Name: "cpu"}, true, "official-default-cpu"
	}
	switch kind {
	case "cpu":
		return Backend{Name: "cpu"}, true, "filename-rule"
	case "vulkan":
		return Backend{Name: "vulkan"}, true, "filename-rule"
	case "cuda":
		if qual == "" {
			return Backend{}, false, ""
		}
		return Backend{Name: "cuda", Version: qual}, true, "filename-rule"
	case "rocm":
		if qual == "" {
			return Backend{}, false, ""
		}
		return Backend{Name: "rocm", Version: qual}, true, "filename-rule"
	case "openvino":
		if qual == "" {
			return Backend{}, false, ""
		}
		return Backend{Name: "openvino", Version: qual}, true, "filename-rule"
	case "sycl":
		if qual != "fp16" && qual != "fp32" && qual != "" {
			return Backend{}, false, ""
		}
		return Backend{Name: "sycl", Precision: qual}, true, "filename-rule"
	case "hip":
		return Backend{Name: "hip", Flavor: qual}, true, "filename-rule"
	case "opencl":
		if qual != "adreno" {
			return Backend{}, false, ""
		}
		return Backend{Name: "opencl", Flavor: "adreno"}, true, "filename-rule"
	default:
		return Backend{}, false, ""
	}
}
