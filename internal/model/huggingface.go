package model

import (
	"encoding/json"
	"fmt"
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
	// LFSOID is the LFS content hash (oid) Hugging Face reports for this
	// sibling, when one is available. It is not part of the persisted
	// sidecar shape, only used transiently to verify content identity during
	// reconciliation, so it is excluded from JSON output.
	LFSOID string `json:"-"`
}
type hfInfo struct {
	SHA      string `json:"sha"`
	Siblings []struct {
		RFilename string `json:"rfilename"`
		Size      int64  `json:"size"`
		LFS       *struct {
			OID string `json:"oid"`
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

func (r Resolver) resolveRepository(repo, requested, filename string) ([]Candidate, error) {
	client := r.Client
	if client == nil {
		client = http.DefaultClient
	}
	base := strings.TrimRight(r.BaseURL, "/")
	if base == "" {
		base = "https://huggingface.co"
	}
	q, e := http.NewRequest(http.MethodGet, base+"/api/models/"+repo+"/revision/"+url.PathEscape(requested), nil)
	if e != nil {
		return nil, e
	}
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
		if filename != "" {
			if strings.HasSuffix(strings.ToLower(filename), ".gguf") {
				if !strings.EqualFold(s.RFilename, filename) {
					continue
				}
			} else if !strings.Contains(strings.ToLower(s.RFilename), strings.ToLower(filename)) {
				continue
			}
		}
		src := Source{Provider: "huggingface", Repository: repo, RequestedRevision: requested, ResolvedRevision: info.SHA, Filename: s.RFilename, URL: base + "/" + repo + "/resolve/" + info.SHA + "/" + pathEscape(s.RFilename)}
		artifact := Artifact{SizeBytes: s.Size}
		var lfsOID string
		if s.LFS != nil {
			lfsOID = s.LFS.OID
		}
		if head, headErr := http.NewRequest(http.MethodHead, src.URL, nil); headErr == nil {
			if response, requestErr := client.Do(head); requestErr == nil {
				if response.StatusCode >= 200 && response.StatusCode < 300 {
					if response.ContentLength > 0 {
						artifact.SizeBytes = response.ContentLength
					}
					artifact.ETag = response.Header.Get("ETag")
					artifact.LastModified = response.Header.Get("Last-Modified")
				}
				_ = response.Body.Close()
			}
		}
		out = append(out, Candidate{src, artifact, lfsOID})
	}
	if filename != "" && len(out) != 1 {
		matched := make([]string, 0, len(out))
		for _, c := range out {
			matched = append(matched, c.Source.Filename)
		}
		return nil, &ErrSelectorCandidates{Repository: repo, Selector: filename, Matched: matched, Available: available}
	}
	return out, nil
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
}

// Count reports how many artifacts the selector matched.
func (e *ErrSelectorCandidates) Count() int { return len(e.Matched) }

func (e *ErrSelectorCandidates) Error() string {
	// A zero-match selector has no matches worth naming, so the whole
	// repository is offered instead.
	candidates := e.Matched
	if len(candidates) == 0 {
		candidates = e.Available
	}
	return fmt.Sprintf("selector %q resolves to %d GGUF artifacts in %s; candidates: %s", e.Selector, len(e.Matched), e.Repository, formatCandidates(candidates))
}
func parseRef(input, sel string) (string, string, string, error) {
	requested := "main"
	filename := sel
	if u, e := url.Parse(input); e == nil && u.Host == "huggingface.co" {
		p := strings.Split(strings.Trim(u.Path, "/"), "/")
		if len(p) < 5 || p[2] != "resolve" {
			return "", "", "", fmt.Errorf("invalid Hugging Face resolve URL")
		}
		return strings.Join(p[:2], "/"), p[3], strings.Join(p[4:], "/"), nil
	}
	parts := strings.SplitN(input, ":", 2)
	repo := parts[0]
	if len(parts) == 2 && filename == "" {
		filename = parts[1]
	}
	if at := strings.Index(repo, "@"); at != -1 {
		requested = repo[at+1:]
		repo = repo[:at]
	}
	if strings.Count(repo, "/") != 1 {
		return "", "", "", fmt.Errorf("repository must be namespace/name")
	}
	return repo, requested, filename, nil
}
func pathEscape(v string) string { return strings.ReplaceAll(url.PathEscape(v), "%2F", "/") }
