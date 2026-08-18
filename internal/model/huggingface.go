package model

import (
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
)

type Resolver struct {
	Client  *http.Client
	BaseURL string
}
type Candidate struct {
	Source   Source   `json:"source"`
	Artifact Artifact `json:"artifact"`
	// ContentSHA256 is the file-content digest Hugging Face reports for this
	// sibling, when one is available. It is not part of the persisted
	// sidecar shape, only used transiently to verify content identity during
	// reconciliation, so it is excluded from JSON output.
	ContentSHA256 string `json:"-"`
	// ETag is whatever the final HTTP response carried. It is transport state
	// and never evidence: the value's domain changes across redirect hops, so
	// for a Hugging Face artifact it is the Xet CAS hash rather than anything
	// Coquena can reproduce or compare. It lives here, and on Job, because a
	// resumable download needs an opaque validator for If-Range — and it is
	// excluded from JSON so it can never reach a sidecar.
	ETag string `json:"-"`
}
type hfInfo struct {
	SHA      string `json:"sha"`
	Siblings []struct {
		RFilename string `json:"rfilename"`
		Size      int64  `json:"size"`
		LFS       *struct {
			SHA256 string `json:"sha256"`
		} `json:"lfs"`
	} `json:"siblings"`
}

func (r Resolver) Resolve(input, selector string) ([]Candidate, error) {
	repo, requested, filename, e := parseRef(input, selector)
	if e != nil {
		return nil, e
	}
	return r.resolveRepository(repo, requested, filename)
}

func (r Resolver) ResolveRevision(repository, requestedRevision, selector string) ([]Candidate, error) {
	if strings.Count(repository, "/") != 1 || requestedRevision == "" {
		return nil, fmt.Errorf("repository must be namespace/name with a requested revision")
	}
	return r.resolveRepository(repository, requestedRevision, selector)
}

// SelectorMatches reports whether filename satisfies selector under the single
// rule the whole product applies: a selector ending in .gguf matches by exact
// filename equality, any other selector matches by case-insensitive substring,
// and an empty selector matches everything. Discovery marks the candidate a
// selector would resolve to without re-resolving the repository, so the rule
// lives here rather than being restated at each call site.
func SelectorMatches(selector, filename string) bool {
	if selector == "" {
		return true
	}
	if strings.HasSuffix(strings.ToLower(selector), ".gguf") {
		return strings.EqualFold(filename, selector)
	}
	return strings.Contains(strings.ToLower(filename), strings.ToLower(selector))
}

// ParseReference splits a repository reference or a canonical Hugging Face
// resolve URL into its repository, requested revision, and selector, without
// contacting any network service. Discovery needs the three parts separately:
// it resolves the repository with no selector so every candidate is reported,
// and applies the selector afterwards only to mark one of them.
func ParseReference(input string) (repository, revision, selector string, err error) {
	return parseRef(input, "")
}

// endpoint reports the HTTP client and base URL every Hugging Face read uses,
// defaulting to the shared client and the public host.
func (r Resolver) endpoint() (*http.Client, string) {
	client := r.Client
	if client == nil {
		client = http.DefaultClient
	}
	base := strings.TrimRight(r.BaseURL, "/")
	if base == "" {
		base = "https://huggingface.co"
	}
	return client, base
}

func (r Resolver) resolveRepository(repo, requested, filename string) ([]Candidate, error) {
	client, base := r.endpoint()
	q, e := http.NewRequest(http.MethodGet, base+"/api/models/"+repo+"/revision/"+url.PathEscape(requested), nil)
	if e != nil {
		return nil, e
	}
	query := q.URL.Query()
	query.Set("blobs", "true")
	q.URL.RawQuery = query.Encode()
	resp, e := client.Do(q)
	if e != nil {
		return nil, e
	}
	defer resp.Body.Close()
	if resp.StatusCode != 200 {
		return nil, fmt.Errorf("resolve Hugging Face repository: %s", resp.Status)
	}
	var info hfInfo
	if e = json.NewDecoder(resp.Body).Decode(&info); e != nil {
		return nil, e
	}
	if info.SHA == "" {
		return nil, fmt.Errorf("Hugging Face did not return a commit SHA")
	}
	var out []Candidate
	var available []string
	for _, s := range info.Siblings {
		if !isGGUF(s.RFilename) {
			continue
		}
		available = append(available, s.RFilename)
		if !SelectorMatches(filename, s.RFilename) {
			continue
		}
		src := Source{Provider: "huggingface", Repository: repo, RequestedRevision: requested, ResolvedRevision: info.SHA, Filename: s.RFilename, URL: base + "/" + repo + "/resolve/" + info.SHA + "/" + pathEscape(s.RFilename)}
		artifact := Artifact{SizeBytes: s.Size}
		var contentSHA256 string
		if s.LFS != nil {
			contentSHA256 = strings.ToLower(s.LFS.SHA256)
			if contentSHA256 != "" {
				decoded, decodeErr := hex.DecodeString(contentSHA256)
				if decodeErr != nil || len(decoded) != 32 {
					return nil, fmt.Errorf("resolve Hugging Face artifact %q: invalid lfs.sha256 %q", s.RFilename, s.LFS.SHA256)
				}
			}
		}
		var etag string
		if head, headErr := http.NewRequest(http.MethodHead, src.URL, nil); headErr == nil {
			if response, requestErr := client.Do(head); requestErr == nil {
				if response.StatusCode >= 200 && response.StatusCode < 300 {
					if response.ContentLength > 0 {
						artifact.SizeBytes = response.ContentLength
					}
					etag = response.Header.Get("ETag")
					artifact.LastModified = response.Header.Get("Last-Modified")
				}
				_ = response.Body.Close()
			}
		}
		out = append(out, Candidate{Source: src, Artifact: artifact, ContentSHA256: contentSHA256, ETag: etag})
	}
	if filename != "" && len(out) != 1 {
		matched := make([]string, 0, len(out))
		for _, c := range out {
			matched = append(matched, c.Source.Filename)
		}
		references := make([]string, 0, len(out))
		sourceNames := matched
		if len(sourceNames) == 0 {
			sourceNames = available
		}
		for _, name := range sourceNames {
			references = append(references, CanonicalReference(Source{
				Provider:         "huggingface",
				Repository:       repo,
				ResolvedRevision: info.SHA,
				Filename:         name,
			}))
		}
		return nil, &ErrSelectorCandidates{Repository: repo, Selector: filename, Matched: matched, Available: available, References: references}
	}
	return out, nil
}

// maxModelCardBytes bounds how much of a repository README is read. A model
// card is prose meant for a human, so a publisher shipping a pathological one
// must not be able to make discovery allocate without limit.
const maxModelCardBytes = 1 << 20

// ModelCard reads the repository's README.md at the given revision and returns
// it as plain text, with the YAML front matter stripped: that block is
// publication metadata rather than anything the publisher wrote for a reader.
//
// A repository publishing no model card is not an error, so a 404 returns the
// empty string and no error. Any other non-200 response is reported, and the
// caller decides whether it is fatal.
//
// No caller reaches this today: discovery stopped reporting the model card
// because its length buries the candidate list. It is kept, unreferenced and
// deliberately, for the later change that reintroduces the output behind an
// explicit option — unreachable here is a decision, not an abandoned
// implementation.
func (r Resolver) ModelCard(repository, revision string) (string, error) {
	if strings.Count(repository, "/") != 1 || revision == "" {
		return "", fmt.Errorf("repository must be namespace/name with a resolved revision")
	}
	client, base := r.endpoint()
	q, e := http.NewRequest(http.MethodGet, base+"/"+repository+"/resolve/"+url.PathEscape(revision)+"/README.md", nil)
	if e != nil {
		return "", e
	}
	resp, e := client.Do(q)
	if e != nil {
		return "", e
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return "", nil
	}
	if resp.StatusCode != 200 {
		return "", fmt.Errorf("read Hugging Face model card: %s", resp.Status)
	}
	body, e := io.ReadAll(io.LimitReader(resp.Body, maxModelCardBytes))
	if e != nil {
		return "", e
	}
	return stripFrontMatter(string(body)), nil
}

// stripFrontMatter removes the leading `---`-delimited YAML block a Hugging
// Face model card conventionally opens with. An unterminated block is left
// untouched rather than swallowing the whole document.
func stripFrontMatter(card string) string {
	trimmed := strings.TrimPrefix(card, "\ufeff")
	if !strings.HasPrefix(trimmed, "---\n") && !strings.HasPrefix(trimmed, "---\r\n") {
		return card
	}
	rest := trimmed[strings.Index(trimmed, "\n")+1:]
	for _, terminator := range []string{"\n---\n", "\n---\r\n"} {
		if end := strings.Index(rest, terminator); end != -1 {
			return strings.TrimLeft(rest[end+len(terminator):], "\r\n")
		}
	}
	if strings.HasPrefix(rest, "---\n") || strings.HasPrefix(rest, "---\r\n") {
		return strings.TrimLeft(rest[strings.Index(rest, "\n")+1:], "\r\n")
	}
	return card
}

// ErrSelectorCandidates reports a selector that resolved to zero or several
// GGUF artifacts in a repository. It carries the candidate filenames rather
// than only their count, because a caller cannot offer the operator a choice
// between filenames it was never given.
type ErrSelectorCandidates struct {
	Repository string
	Selector   string
	// Matched holds the filenames the selector matched; it is empty when the
	// selector matched nothing.
	Matched []string
	// Available holds every GGUF filename in the repository, so a zero-match
	// diagnostic can still name what the operator may choose from.
	Available []string
	// References holds actionable canonical exact-filename references for the
	// matched set, or for all available artifacts on a zero match.
	References []string
}

// Count reports how many artifacts the selector matched.
func (e *ErrSelectorCandidates) Count() int { return len(e.Matched) }

func (e *ErrSelectorCandidates) Error() string {
	// A zero-match selector has no matches worth naming, so the whole
	// repository is offered instead.
	candidates := e.References
	if len(candidates) == 0 {
		candidates = e.Matched
		if len(candidates) == 0 {
			candidates = e.Available
		}
	}
	return fmt.Sprintf("selector %q resolves to %d GGUF artifacts in %s; candidates: %s", e.Selector, len(e.Matched), e.Repository, formatCandidates(candidates))
}
func parseRef(input, sel string) (string, string, string, error) {
	ref, err := ParseModelReference(input)
	if err != nil {
		return "", "", "", err
	}
	selector := ref.Selector
	if sel != "" {
		selector = sel
	}
	return ref.Repository, ref.RequestedRevision(), selector, nil
}
func pathEscape(v string) string { return strings.ReplaceAll(url.PathEscape(v), "%2F", "/") }
