package llamacpp

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"
)

const DefaultAPIURL = "https://api.github.com/repos/ggml-org/llama.cpp"
const MaxHistory = 100

type GitHubClient struct {
	BaseURL string
	HTTP    *http.Client
	Token   string
}

func NewGitHubClient(token string) *GitHubClient {
	if token == "" {
		token = os.Getenv("GITHUB_TOKEN")
	}
	return &GitHubClient{BaseURL: DefaultAPIURL, Token: token, HTTP: &http.Client{Timeout: 15 * time.Second, Transport: &http.Transport{Proxy: http.ProxyFromEnvironment}}}
}

func (c *GitHubClient) Latest(ctx context.Context) (Release, error) {
	return c.get(ctx, "/releases/latest")
}
func (c *GitHubClient) ByTag(ctx context.Context, tag string) (Release, error) {
	if !validTag(tag) {
		return Release{}, fmt.Errorf("invalid release tag %q", tag)
	}
	return c.get(ctx, "/releases/tags/"+url.PathEscape(tag))
}
func (c *GitHubClient) History(ctx context.Context, count int) ([]Release, error) {
	if count < 1 || count > MaxHistory {
		return nil, fmt.Errorf("history count must be between 1 and %d", MaxHistory)
	}
	out := []Release{}
	page := 1
	for len(out) < count {
		n := 100
		var batch []Release
		if err := c.request(ctx, fmt.Sprintf("/releases?per_page=%d&page=%d", n, page), &batch); err != nil {
			return nil, err
		}
		if len(batch) == 0 {
			break
		}
		for _, r := range batch {
			if !r.Draft && !r.Prerelease && validTag(r.Tag) {
				out = append(out, r)
				if len(out) == count {
					break
				}
			}
		}
		page++
	}
	return out, nil
}
func (c *GitHubClient) get(ctx context.Context, path string) (Release, error) {
	var r Release
	if err := c.request(ctx, path, &r); err != nil {
		return Release{}, err
	}
	if r.Draft || r.Prerelease {
		return Release{}, errors.New("GitHub returned an ineligible release")
	}
	return r, nil
}
func (c *GitHubClient) request(ctx context.Context, path string, out any) error {
	if c.HTTP == nil {
		return errors.New("GitHub client has no HTTP client")
	}
	base := strings.TrimRight(c.BaseURL, "/")
	if base == "" {
		base = DefaultAPIURL
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, base+path, nil)
	if err != nil {
		return err
	}
	req.Header.Set("Accept", "application/vnd.github+json")
	req.Header.Set("X-GitHub-Api-Version", "2022-11-28")
	req.Header.Set("User-Agent", "coquena")
	if c.Token != "" {
		req.Header.Set("Authorization", "Bearer "+c.Token)
	}
	resp, err := c.HTTP.Do(req)
	if err != nil {
		return fmt.Errorf("query llama.cpp releases: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		b, _ := io.ReadAll(io.LimitReader(resp.Body, 4096))
		msg := strings.TrimSpace(string(b))
		rate := ""
		if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusTooManyRequests {
			rate = "; rate limit remaining=" + resp.Header.Get("X-RateLimit-Remaining")
			if reset := resp.Header.Get("X-RateLimit-Reset"); reset != "" {
				if n, e := strconv.ParseInt(reset, 10, 64); e == nil {
					rate += ", resets=" + time.Unix(n, 0).UTC().Format(time.RFC3339)
				}
			}
		}
		return fmt.Errorf("GitHub releases request failed: %s%s: %s", resp.Status, rate, msg)
	}
	if err := json.NewDecoder(io.LimitReader(resp.Body, 16<<20)).Decode(out); err != nil {
		return fmt.Errorf("decode GitHub releases response: %w", err)
	}
	return nil
}
