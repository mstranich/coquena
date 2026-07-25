package main

import (
	"context"
	"fmt"
	"os"

	runtimeapp "github.com/mstranich/coquena/internal/runtime"
)

// runtimeCLIAdapter keeps command grammar/output separate from shared policy.
type runtimeCLIAdapter struct{ service runtimeapp.Service }

func newRuntimeManager() runtimeService { return runtimeCLIAdapter{service: runtimeapp.NewManager()} }

func (a runtimeCLIAdapter) Remote(query runtimeQuery) ([]runtimeRecord, error) {
	return a.service.Remote(context.Background(), query)
}

func (a runtimeCLIAdapter) Download(request runtimeDownloadRequest) (runtimeMutation, error) {
	currentAsset := ""
	return a.service.Download(context.Background(), request, func(progress runtimeapp.Progress) {
		if progress.Phase == "downloading" && progress.AssetID != currentAsset {
			currentAsset = progress.AssetID
			_, _ = fmt.Fprintf(os.Stderr, "Downloading %s\n", currentAsset)
		}
	})
}

func (a runtimeCLIAdapter) List(selector string) ([]runtimeRecord, error) {
	return a.service.List(context.Background(), selector)
}

func (a runtimeCLIAdapter) Remove(id string) (runtimeMutation, error) {
	return a.service.Remove(context.Background(), id)
}

func (a runtimeCLIAdapter) sharedRuntimeService() runtimeapp.Service { return a.service }
