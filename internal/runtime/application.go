// Package runtime defines the application boundary shared by Coquena's user
// interfaces. Runtime adapters own presentation only; policy stays here.
package runtime

import (
	"context"
	"errors"
	"strings"

	"github.com/mstranich/coquena/internal/runtime/llamacpp"
)

type Query struct {
	Release  string
	Selector string
	History  int
}

type DownloadRequest struct{ AssetID, Release, Selector string }

type Record struct {
	Release       string   `json:"release"`
	Target        string   `json:"target"`
	Backend       string   `json:"backend"`
	AssetID       string   `json:"assetId"`
	Dependencies  []string `json:"dependencies,omitempty"`
	Compatibility string   `json:"compatibility,omitempty"`
	Evidence      []string `json:"evidence,omitempty"`
}

type Mutation struct {
	Runtime Record `json:"runtime"`
	Changed bool   `json:"changed"`
}

// Service is deliberately interface-neutral: CLI, WebUI and a future TUI use
// the same operations and apply their own presentation/transport concerns.
type Service interface {
	Remote(context.Context, Query) ([]Record, error)
	Download(context.Context, DownloadRequest, func(Progress)) (Mutation, error)
	List(context.Context, string) ([]Record, error)
	Remove(context.Context, string) (Mutation, error)
}

type Progress struct {
	Phase            string `json:"phase"`
	AssetID          string `json:"assetId,omitempty"`
	AssetTransferred int64  `json:"assetTransferredBytes,omitempty"`
	AssetTotal       int64  `json:"assetTotalBytes,omitempty"`
	TotalTransferred int64  `json:"totalTransferredBytes,omitempty"`
	TotalBytes       int64  `json:"totalBytes,omitempty"`
}

// ValidateExactAssetID performs request-boundary validation before any job is
// created. Manager resolution still verifies official provenance and host fit.
func ValidateExactAssetID(id string) error {
	if err := llamacpp.ValidateAssetID(id); err != nil {
		return err
	}
	if !strings.HasPrefix(id, "llama-") || (!strings.HasSuffix(id, ".zip") && !strings.HasSuffix(id, ".tar.gz")) {
		return errors.New("asset ID must name a llama.cpp runtime archive")
	}
	return nil
}
