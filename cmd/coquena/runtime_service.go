package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/signal"

	modelapp "github.com/mstranich/coquena/internal/model"
	runtimeapp "github.com/mstranich/coquena/internal/runtime"
)

// runtimeCLIAdapter keeps command grammar/output separate from shared policy.
type runtimeCLIAdapter struct{ service runtimeapp.Service }

// runtimeDownloadPaused reports a user interrupt after the runtime store has
// persisted the resumable partial download state.
type runtimeDownloadPaused struct{ assetID string }

func (e runtimeDownloadPaused) Error() string { return "runtime download paused" }

func newRuntimeManager() runtimeService { return runtimeCLIAdapter{service: runtimeapp.NewManager()} }

func (a runtimeCLIAdapter) Remote(query runtimeQuery) ([]runtimeRecord, error) {
	return a.service.Remote(context.Background(), query)
}

func (a runtimeCLIAdapter) Download(request runtimeDownloadRequest) (runtimeMutation, error) {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	jobID := ""
	lastPercent := -1.0
	mutation, err := a.service.Download(ctx, request, func(progress runtimeapp.Progress) {
		if progress.Phase != "downloading" {
			return
		}
		if jobID == "" {
			jobID = progress.AssetID
			_, _ = fmt.Fprintf(os.Stderr, "ID: %s\n", jobID)
		}
		if progress.TotalBytes <= 0 {
			return
		}
		percent := modelapp.DownloadPercent(progress.TotalTransferred, progress.TotalBytes)
		if percent-lastPercent < 1 && percent < 100 {
			return
		}
		lastPercent = percent
		_, _ = fmt.Fprintf(os.Stderr, "Downloading %s: %.1f%% (%s / %s)\n", progress.AssetID, percent, modelapp.HumanBytes(progress.TotalTransferred), modelapp.HumanBytes(progress.TotalBytes))
	})
	if errors.Is(err, context.Canceled) {
		return runtimeMutation{}, runtimeDownloadPaused{assetID: jobID}
	}
	return mutation, err
}

func (a runtimeCLIAdapter) List(selector string) ([]runtimeRecord, error) {
	return a.service.List(context.Background(), selector)
}

func (a runtimeCLIAdapter) Remove(id string) (runtimeMutation, error) {
	return a.service.Remove(context.Background(), id)
}

func (a runtimeCLIAdapter) sharedRuntimeService() runtimeapp.Service { return a.service }
