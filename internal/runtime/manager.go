package runtime

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"os"
	"regexp"
	"strings"
	"time"

	"github.com/mstranich/coquena/internal/installation"
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

// Manager owns runtime discovery, compatibility and local artifact mutation.
// It is presentation-neutral and is shared by CLI, WebUI and future TUI adapters.
type Manager struct {
	releases   releaseSource
	httpClient *http.Client
	inspect    func() (hostsystem.Report, error)
	root       func() (string, error)
}

func NewManager() *Manager {
	client := llamacpp.NewGitHubClient(os.Getenv("GITHUB_TOKEN"))
	return &Manager{releases: client, httpClient: client.HTTP, inspect: hostsystem.Inspect, root: runtimeRoot}
}

func runtimeRoot() (string, error) { return installation.Home() }

func (m *Manager) host() (llamacpp.Host, error) {
	report, err := m.inspect()
	if err != nil {
		return llamacpp.Host{}, fmt.Errorf("inspect host: %w", err)
	}
	return llamacpp.HostFromReport(report)
}

func (m *Manager) catalog(ctx context.Context, query Query) ([]llamacpp.Distribution, error) {
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

func (m *Manager) Remote(ctx context.Context, query Query) ([]Record, error) {
	distributions, err := m.catalog(ctx, query)
	if err != nil {
		return nil, err
	}
	return records(distributions, "compatible"), nil
}

func (m *Manager) resolveDownload(ctx context.Context, request DownloadRequest) (llamacpp.Distribution, error) {
	if request.AssetID != "" {
		if err := llamacpp.ValidateAssetID(request.AssetID); err != nil {
			return llamacpp.Distribution{}, err
		}
		match := assetTagPattern.FindStringSubmatch(request.AssetID)
		if match == nil {
			return llamacpp.Distribution{}, errors.New("asset ID does not identify an official primary runtime release")
		}
		distributions, err := m.catalog(ctx, Query{Release: match[1]})
		if err != nil {
			return llamacpp.Distribution{}, err
		}
		return llamacpp.ResolveExact(distributions, request.AssetID)
	}
	distributions, err := m.catalog(ctx, Query{Release: request.Release, Selector: request.Selector})
	if err != nil {
		return llamacpp.Distribution{}, err
	}
	selection, err := llamacpp.ParseSelection(selectionArgs(request.Release, request.Selector, false))
	if err != nil {
		return llamacpp.Distribution{}, err
	}
	return llamacpp.Resolve(distributions, selection)
}

func emit(progress func(Progress), value Progress) {
	if progress != nil {
		progress(value)
	}
}

func (m *Manager) Download(ctx context.Context, request DownloadRequest, progress func(Progress)) (Mutation, error) {
	distribution, err := m.resolveDownload(ctx, request)
	if err != nil {
		return Mutation{}, err
	}
	root, err := m.root()
	if err != nil {
		return Mutation{}, err
	}
	layout, err := runtimestore.Open(root)
	if err != nil {
		return Mutation{}, err
	}
	lock, err := runtimestore.Acquire(layout)
	if err != nil {
		return Mutation{}, err
	}
	defer lock.Release()
	stage, err := runtimestore.NewStage(layout)
	if err != nil {
		return Mutation{}, err
	}
	keepStage := false
	defer func() {
		if !keepStage {
			_ = os.RemoveAll(stage)
		}
	}()
	assets := append([]llamacpp.ReleaseAsset{distribution.Primary}, distribution.Dependencies...)
	var totalBytes int64
	for _, asset := range assets {
		totalBytes += asset.Size
	}
	var completedBytes int64
	metadataAssets := make([]runtimestore.Asset, 0, len(assets))
	for _, asset := range assets {
		stored := runtimestore.Asset{Name: asset.Name, URL: asset.URL, Size: asset.Size, Digest: asset.Digest}
		archive, downloadErr := runtimestore.DownloadWithProgress(ctx, m.httpClient, layout, stored, runtimestore.Limits{}, func(transferred int64) {
			emit(progress, Progress{Phase: "downloading", AssetID: asset.Name, AssetTransferred: transferred, AssetTotal: asset.Size, TotalTransferred: completedBytes + transferred, TotalBytes: totalBytes})
		})
		if downloadErr != nil {
			return Mutation{}, fmt.Errorf("download %s: %w", asset.Name, downloadErr)
		}
		completedBytes += asset.Size
		emit(progress, Progress{Phase: "verifying", AssetID: asset.Name, AssetTransferred: asset.Size, AssetTotal: asset.Size, TotalTransferred: completedBytes, TotalBytes: totalBytes})
		emit(progress, Progress{Phase: "extracting", AssetID: asset.Name, TotalTransferred: completedBytes, TotalBytes: totalBytes})
		extractErr := extractArchive(asset.Name, archive, stage)
		_ = os.Remove(archive)
		if extractErr != nil {
			return Mutation{}, fmt.Errorf("extract %s: %w", asset.Name, extractErr)
		}
		metadataAssets = append(metadataAssets, stored)
	}
	emit(progress, Progress{Phase: "probing", TotalTransferred: completedBytes, TotalBytes: totalBytes})
	if _, err = runtimestore.FindAndProbeServer(ctx, stage, 10*time.Second); err != nil {
		return Mutation{}, err
	}
	metadata := runtimestore.Metadata{ID: distribution.Primary.Name, Identity: runtimestore.Identity{Release: distribution.Release, OS: distribution.OS, Architecture: distribution.Architecture, Target: string(distribution.Target), Backend: distribution.Backend.Name, BackendVersion: distribution.Backend.Version}, Assets: metadataAssets}
	emit(progress, Progress{Phase: "publishing", TotalTransferred: completedBytes, TotalBytes: totalBytes})
	already, err := runtimestore.Publish(layout, stage, metadata)
	if err != nil {
		return Mutation{}, err
	}
	keepStage = true
	return Mutation{Runtime: record(distribution, "compatible"), Changed: !already}, nil
}

func extractArchive(assetName, archive, destination string) error {
	if strings.HasSuffix(strings.ToLower(assetName), ".tar.gz") {
		return runtimestore.ExtractTarGZ(archive, destination, runtimestore.Limits{})
	}
	return runtimestore.ExtractZIP(archive, destination, runtimestore.Limits{})
}

func (m *Manager) List(_ context.Context, selector string) ([]Record, error) {
	var backend *llamacpp.BackendSelector
	if selector != "" {
		parsed, err := llamacpp.ParseBackendSelector(selector)
		if err != nil {
			return nil, err
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
	out := make([]Record, 0, len(entries))
	for _, entry := range entries {
		identity := entry.Metadata.Identity
		distribution := llamacpp.Distribution{Release: identity.Release, Target: llamacpp.Target(identity.Target), OS: identity.OS, Architecture: identity.Architecture, Backend: llamacpp.Backend{Name: identity.Backend, Version: identity.BackendVersion}, Primary: llamacpp.ReleaseAsset{Name: entry.Metadata.ID, Kind: llamacpp.AssetPrimary}}
		if backend != nil && !backend.Match(distribution.Backend) {
			continue
		}
		compatibility := llamacpp.EvaluateCompatibility(distribution, host)
		dependencies := []string{}
		for _, asset := range entry.Metadata.Assets[1:] {
			dependencies = append(dependencies, asset.Name)
		}
		record := record(distribution, string(compatibility.State))
		record.Dependencies = dependencies
		record.Evidence = compatibility.Evidence
		out = append(out, record)
	}
	return out, nil
}

func (m *Manager) Remove(_ context.Context, id string) (Mutation, error) {
	if err := llamacpp.ValidateAssetID(id); err != nil {
		return Mutation{}, err
	}
	root, err := m.root()
	if err != nil {
		return Mutation{}, err
	}
	layout, err := runtimestore.Open(root)
	if err != nil {
		return Mutation{}, err
	}
	entries, _, err := runtimestore.Scan(layout)
	if err != nil {
		return Mutation{}, err
	}
	var found *runtimestore.Metadata
	for i := range entries {
		if entries[i].Metadata.ID == id {
			found = &entries[i].Metadata
			break
		}
	}
	if found == nil {
		return Mutation{}, os.ErrNotExist
	}
	lock, err := runtimestore.Acquire(layout)
	if err != nil {
		return Mutation{}, err
	}
	defer lock.Release()
	if err := runtimestore.Remove(layout, id); err != nil {
		return Mutation{}, err
	}
	distribution := llamacpp.Distribution{Release: found.Identity.Release, Target: llamacpp.Target(found.Identity.Target), OS: found.Identity.OS, Architecture: found.Identity.Architecture, Backend: llamacpp.Backend{Name: found.Identity.Backend, Version: found.Identity.BackendVersion}, Primary: llamacpp.ReleaseAsset{Name: id, Kind: llamacpp.AssetPrimary}}
	return Mutation{Runtime: record(distribution, ""), Changed: true}, nil
}

func records(distributions []llamacpp.Distribution, compatibility string) []Record {
	out := make([]Record, len(distributions))
	for i := range distributions {
		out[i] = record(distributions[i], compatibility)
	}
	return out
}

func record(distribution llamacpp.Distribution, compatibility string) Record {
	dependencies := make([]string, len(distribution.Dependencies))
	for i := range distribution.Dependencies {
		dependencies[i] = distribution.Dependencies[i].Name
	}
	return Record{Release: distribution.Release, Target: string(distribution.Target), Backend: distribution.Backend.String(), AssetID: distribution.Primary.Name, Dependencies: dependencies, Compatibility: compatibility}
}
