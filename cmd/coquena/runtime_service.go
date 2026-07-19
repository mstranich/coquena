package main

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/mstranich/coquena/internal/runtime/llamacpp"
	runtimestore "github.com/mstranich/coquena/internal/runtime/store"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

var assetTagPattern = regexp.MustCompile(`^llama-(b[1-9][0-9]*)-bin-`)

type releaseSource interface {
	Latest(context.Context) (llamacpp.Release, error)
	ByTag(context.Context, string) (llamacpp.Release, error)
	History(context.Context, int) ([]llamacpp.Release, error)
}

type runtimeManager struct {
	releases   releaseSource
	httpClient *http.Client
	inspect    func() (hostsystem.Report, error)
	root       func() (string, error)
	progress   io.Writer
}

func newRuntimeManager() *runtimeManager {
	client := llamacpp.NewGitHubClient(os.Getenv("GITHUB_TOKEN"))
	return &runtimeManager{releases: client, httpClient: client.HTTP, inspect: hostsystem.Inspect, root: runtimeRoot, progress: os.Stderr}
}

func runtimeRoot() (string, error) {
	executable, err := os.Executable()
	if err != nil {
		return "", fmt.Errorf("resolve executable: %w", err)
	}
	resolved, err := filepath.EvalSymlinks(executable)
	if err != nil {
		return "", fmt.Errorf("resolve executable link: %w", err)
	}
	absolute, err := filepath.Abs(resolved)
	if err != nil {
		return "", err
	}
	return filepath.Dir(absolute), nil
}

func (m *runtimeManager) host() (llamacpp.Host, error) {
	report, err := m.inspect()
	if err != nil {
		return llamacpp.Host{}, fmt.Errorf("inspect host: %w", err)
	}
	return llamacpp.HostFromReport(report)
}

func (m *runtimeManager) catalog(ctx context.Context, query runtimeQuery) ([]llamacpp.Distribution, error) {
	var releases []llamacpp.Release
	if query.History > 0 {
		items, err := m.releases.History(ctx, query.History)
		if err != nil {
			return nil, err
		}
		releases = items
	} else if query.Release == "" || query.Release == "latest" {
		item, err := m.releases.Latest(ctx)
		if err != nil {
			return nil, err
		}
		releases = []llamacpp.Release{item}
	} else {
		item, err := m.releases.ByTag(ctx, query.Release)
		if err != nil {
			return nil, err
		}
		releases = []llamacpp.Release{item}
	}
	host, err := m.host()
	if err != nil {
		return nil, err
	}
	selection, err := llamacpp.ParseSelection(selectionArgs(query.Release, query.Selector, query.History > 0))
	if err != nil {
		return nil, err
	}
	var compatible []llamacpp.Distribution
	for _, release := range releases {
		catalog := llamacpp.ClassifyRelease(release)
		for _, item := range llamacpp.CompatibleOnly(catalog.Distributions, host) {
			compatible = append(compatible, item.Distribution)
		}
	}
	return llamacpp.Filter(compatible, selection), nil
}

func selectionArgs(release, selector string, history bool) []string {
	if history {
		if selector == "" {
			return nil
		}
		return []string{selector}
	}
	if release == "" || release == "latest" {
		if selector == "" {
			return nil
		}
		return []string{selector}
	}
	if selector == "" {
		return []string{release}
	}
	return []string{release, selector}
}

func (m *runtimeManager) Remote(q runtimeQuery) ([]runtimeRecord, error) {
	ds, err := m.catalog(context.Background(), q)
	if err != nil {
		return nil, err
	}
	return records(ds, "compatible"), nil
}

func (m *runtimeManager) resolveDownload(ctx context.Context, request runtimeDownloadRequest) (llamacpp.Distribution, error) {
	if request.AssetID != "" {
		if err := llamacpp.ValidateAssetID(request.AssetID); err != nil {
			return llamacpp.Distribution{}, err
		}
		match := assetTagPattern.FindStringSubmatch(request.AssetID)
		if match == nil {
			return llamacpp.Distribution{}, errors.New("asset ID does not identify an official primary runtime release")
		}
		ds, err := m.catalog(ctx, runtimeQuery{Release: match[1]})
		if err != nil {
			return llamacpp.Distribution{}, err
		}
		return llamacpp.ResolveExact(ds, request.AssetID)
	}
	ds, err := m.catalog(ctx, runtimeQuery{Release: request.Release, Selector: request.Selector})
	if err != nil {
		return llamacpp.Distribution{}, err
	}
	selection, err := llamacpp.ParseSelection(selectionArgs(request.Release, request.Selector, false))
	if err != nil {
		return llamacpp.Distribution{}, err
	}
	return llamacpp.Resolve(ds, selection)
}

func (m *runtimeManager) Download(request runtimeDownloadRequest) (runtimeMutation, error) {
	ctx := context.Background()
	distribution, err := m.resolveDownload(ctx, request)
	if err != nil {
		return runtimeMutation{}, err
	}
	root, err := m.root()
	if err != nil {
		return runtimeMutation{}, err
	}
	layout, err := runtimestore.Open(root)
	if err != nil {
		return runtimeMutation{}, err
	}
	lock, err := runtimestore.Acquire(layout)
	if err != nil {
		return runtimeMutation{}, err
	}
	defer lock.Release()
	stage, err := runtimestore.NewStage(layout)
	if err != nil {
		return runtimeMutation{}, err
	}
	keepStage := false
	defer func() {
		if !keepStage {
			_ = os.RemoveAll(stage)
		}
	}()
	assets := append([]llamacpp.ReleaseAsset{distribution.Primary}, distribution.Dependencies...)
	metadataAssets := make([]runtimestore.Asset, 0, len(assets))
	for _, asset := range assets {
		if m.progress != nil {
			_, _ = fmt.Fprintf(m.progress, "Downloading %s\n", asset.Name)
		}
		stored := runtimestore.Asset{Name: asset.Name, URL: asset.URL, Size: asset.Size, Digest: asset.Digest}
		archive, downloadErr := runtimestore.Download(ctx, m.httpClient, layout, stored, runtimestore.Limits{})
		if downloadErr != nil {
			return runtimeMutation{}, fmt.Errorf("download %s: %w", asset.Name, downloadErr)
		}
		extractErr := extractArchive(asset.Name, archive, stage)
		_ = os.Remove(archive)
		if extractErr != nil {
			return runtimeMutation{}, fmt.Errorf("extract %s: %w", asset.Name, extractErr)
		}
		metadataAssets = append(metadataAssets, stored)
	}
	if _, err = runtimestore.FindAndProbeServer(ctx, stage, 10*time.Second); err != nil {
		return runtimeMutation{}, err
	}
	metadata := runtimestore.Metadata{ID: distribution.Primary.Name, Identity: runtimestore.Identity{Release: distribution.Release, OS: distribution.OS, Architecture: distribution.Architecture, Target: string(distribution.Target), Backend: distribution.Backend.Name, BackendVersion: distribution.Backend.Version}, Assets: metadataAssets}
	already, err := runtimestore.Publish(layout, stage, metadata)
	if err != nil {
		return runtimeMutation{}, err
	}
	keepStage = true
	return runtimeMutation{Runtime: record(distribution, "compatible"), Changed: !already}, nil
}

func extractArchive(assetName, archive, destination string) error {
	if strings.HasSuffix(strings.ToLower(assetName), ".tar.gz") {
		return runtimestore.ExtractTarGZ(archive, destination, runtimestore.Limits{})
	}
	return runtimestore.ExtractZIP(archive, destination, runtimestore.Limits{})
}

func (m *runtimeManager) List(selector string) ([]runtimeRecord, error) {
	var backend *llamacpp.BackendSelector
	if selector != "" {
		parsed, parseErr := llamacpp.ParseBackendSelector(selector)
		if parseErr != nil {
			return nil, parseErr
		}
		backend = &parsed
	}
	root, err := m.root()
	if err != nil {
		return nil, err
	}
	layout, err := runtimestore.Open(root)
	if err != nil {
		return nil, err
	}
	entries, diagnostics, err := runtimestore.Scan(layout)
	if err != nil {
		return nil, err
	}
	if len(diagnostics) > 0 {
		return nil, fmt.Errorf("local runtime store contains %d invalid entries; first: %s", len(diagnostics), diagnostics[0].Error)
	}
	host, err := m.host()
	if err != nil {
		return nil, err
	}
	out := make([]runtimeRecord, 0, len(entries))
	for _, entry := range entries {
		identity := entry.Metadata.Identity
		d := llamacpp.Distribution{Release: identity.Release, Target: llamacpp.Target(identity.Target), OS: identity.OS, Architecture: identity.Architecture, Backend: llamacpp.Backend{Name: identity.Backend, Version: identity.BackendVersion}, Primary: llamacpp.ReleaseAsset{Name: entry.Metadata.ID, Kind: llamacpp.AssetPrimary}}
		if backend != nil && !backend.Match(d.Backend) {
			continue
		}
		compatibility := llamacpp.EvaluateCompatibility(d, host)
		deps := []string{}
		for _, asset := range entry.Metadata.Assets[1:] {
			deps = append(deps, asset.Name)
		}
		r := record(d, string(compatibility.State))
		r.Dependencies = deps
		r.Evidence = compatibility.Evidence
		out = append(out, r)
	}
	return out, nil
}

func (m *runtimeManager) Remove(id string) (runtimeMutation, error) {
	if err := llamacpp.ValidateAssetID(id); err != nil {
		return runtimeMutation{}, err
	}
	root, err := m.root()
	if err != nil {
		return runtimeMutation{}, err
	}
	layout, err := runtimestore.Open(root)
	if err != nil {
		return runtimeMutation{}, err
	}
	entries, _, err := runtimestore.Scan(layout)
	if err != nil {
		return runtimeMutation{}, err
	}
	var found *runtimestore.Metadata
	for i := range entries {
		if entries[i].Metadata.ID == id {
			found = &entries[i].Metadata
			break
		}
	}
	if found == nil {
		return runtimeMutation{}, os.ErrNotExist
	}
	lock, err := runtimestore.Acquire(layout)
	if err != nil {
		return runtimeMutation{}, err
	}
	defer lock.Release()
	if err := runtimestore.Remove(layout, id); err != nil {
		return runtimeMutation{}, err
	}
	d := llamacpp.Distribution{Release: found.Identity.Release, Target: llamacpp.Target(found.Identity.Target), OS: found.Identity.OS, Architecture: found.Identity.Architecture, Backend: llamacpp.Backend{Name: found.Identity.Backend, Version: found.Identity.BackendVersion}, Primary: llamacpp.ReleaseAsset{Name: id, Kind: llamacpp.AssetPrimary}}
	return runtimeMutation{Runtime: record(d, ""), Changed: true}, nil
}

func records(ds []llamacpp.Distribution, compatibility string) []runtimeRecord {
	out := make([]runtimeRecord, len(ds))
	for i := range ds {
		out[i] = record(ds[i], compatibility)
	}
	return out
}
func record(d llamacpp.Distribution, compatibility string) runtimeRecord {
	deps := make([]string, len(d.Dependencies))
	for i := range d.Dependencies {
		deps[i] = d.Dependencies[i].Name
	}
	return runtimeRecord{Release: d.Release, Target: string(d.Target), Backend: d.Backend.String(), AssetID: d.Primary.Name, Dependencies: deps, Compatibility: compatibility}
}
