package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/installation"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

type fakeRuntimeService struct {
	remote   func(runtimeQuery) ([]runtimeRecord, error)
	download func(runtimeDownloadRequest) (runtimeMutation, error)
	list     func(string) ([]runtimeRecord, error)
	remove   func(string) (runtimeMutation, error)
}

func (f fakeRuntimeService) Remote(q runtimeQuery) ([]runtimeRecord, error) { return f.remote(q) }
func (f fakeRuntimeService) Download(r runtimeDownloadRequest) (runtimeMutation, error) {
	return f.download(r)
}
func (f fakeRuntimeService) List(selector string) ([]runtimeRecord, error) { return f.list(selector) }
func (f fakeRuntimeService) Remove(id string) (runtimeMutation, error)     { return f.remove(id) }

func runtimeExecute(args []string, svc runtimeService) (int, string, string) {
	var out, err bytes.Buffer
	code := executeWithRuntime(args, &out, &err, func() (installation.Result, error) { return installation.Result{}, nil }, func() (hostsystem.Report, error) { return hostsystem.Report{}, nil }, svc)
	return code, out.String(), err.String()
}

func TestRuntimeRemoteDefaultsToLatestAndPrintsAssetID(t *testing.T) {
	svc := fakeRuntimeService{remote: func(q runtimeQuery) ([]runtimeRecord, error) {
		if q != (runtimeQuery{Release: "latest"}) {
			t.Fatalf("query=%+v", q)
		}
		return []runtimeRecord{{Release: "b10068", Backend: "cuda@13.3", AssetID: "llama-b10068-bin-win-cuda-13.3-x64.zip", Dependencies: []string{"cudart.zip"}}}, nil
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "remote"}, svc)
	if code != 0 || stderr != "" || !strings.Contains(out, "llama-b10068-bin-win-cuda-13.3-x64.zip") || !strings.Contains(out, "Showing only the latest compatible release") || !strings.Contains(out, "--history <count>") {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}

func TestRuntimeRemoteHintIsOnlyForUnqualifiedHumanListing(t *testing.T) {
	svc := fakeRuntimeService{remote: func(runtimeQuery) ([]runtimeRecord, error) { return []runtimeRecord{}, nil }}
	for _, args := range [][]string{{"runtime", "remote", "latest"}, {"runtime", "remote", "cuda"}, {"runtime", "remote", "--history", "2"}} {
		code, out, stderr := runtimeExecute(args, svc)
		if code != 0 || stderr != "" || strings.Contains(out, "Showing only the latest") {
			t.Fatalf("args=%v got (%d,%q,%q)", args, code, out, stderr)
		}
	}
}

func TestRuntimeRemoteParsesTagAndShorthand(t *testing.T) {
	svc := fakeRuntimeService{remote: func(q runtimeQuery) ([]runtimeRecord, error) {
		if q.Release != "b10068" || q.Selector != "cuda@13" {
			t.Fatalf("query=%+v", q)
		}
		return nil, nil
	}}
	code, _, stderr := runtimeExecute([]string{"runtime", "remote", "b10068", "cuda@13"}, svc)
	if code != 0 || stderr != "" {
		t.Fatalf("code=%d stderr=%q", code, stderr)
	}
}

func TestRuntimeRemoteParsesBoundedHistoryAndJSONAnywhere(t *testing.T) {
	svc := fakeRuntimeService{remote: func(q runtimeQuery) ([]runtimeRecord, error) {
		if q.Release != "latest" || q.Selector != "vulkan" || q.History != 20 {
			t.Fatalf("query=%+v", q)
		}
		return []runtimeRecord{}, nil
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "remote", "--json", "--history", "20", "vulkan"}, svc)
	if code != 0 || stderr != "" || strings.TrimSpace(out) != `{"schemaVersion":1,"result":[]}` {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}

func TestRuntimeRemoteRejectsOutOfBoundsHistoryBeforeServiceCall(t *testing.T) {
	called := false
	svc := fakeRuntimeService{remote: func(q runtimeQuery) ([]runtimeRecord, error) {
		called = true
		return nil, nil
	}}
	for _, count := range []string{"0", "101", "nope"} {
		called = false
		code, out, stderr := runtimeExecute([]string{"runtime", "remote", "--history", count}, svc)
		if code == 0 || out != "" || called || !strings.Contains(stderr, "between 1 and 100") {
			t.Fatalf("count=%s got (%d,%q,%q) called=%v", count, code, out, stderr, called)
		}
	}
}

func TestRuntimeDownloadPrefersExactAssetID(t *testing.T) {
	id := "llama-b10068-bin-win-cuda-13.3-x64.zip"
	svc := fakeRuntimeService{download: func(r runtimeDownloadRequest) (runtimeMutation, error) {
		if r.AssetID != id || r.Selector != "" {
			t.Fatalf("request=%+v", r)
		}
		return runtimeMutation{Runtime: runtimeRecord{AssetID: id}, Changed: true}, nil
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "download", id}, svc)
	if code != 0 || stderr != "" || out != "Downloaded "+id+"\n" {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}

func TestRuntimeDownloadUsesLatestForShorthand(t *testing.T) {
	svc := fakeRuntimeService{download: func(r runtimeDownloadRequest) (runtimeMutation, error) {
		if r.Release != "latest" || r.Selector != "cuda@13" || r.AssetID != "" {
			t.Fatalf("request=%+v", r)
		}
		return runtimeMutation{Runtime: runtimeRecord{AssetID: "asset"}, Changed: true}, nil
	}}
	code, _, stderr := runtimeExecute([]string{"runtime", "download", "cuda@13"}, svc)
	if code != 0 || stderr != "" {
		t.Fatalf("code=%d stderr=%q", code, stderr)
	}
}

func TestRuntimeDownloadSurfacesAmbiguityWithoutStdout(t *testing.T) {
	svc := fakeRuntimeService{download: func(runtimeDownloadRequest) (runtimeMutation, error) {
		return runtimeMutation{}, errors.New("selector cuda is ambiguous: asset-12, asset-13")
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "download", "cuda"}, svc)
	if code == 0 || out != "" || !strings.Contains(stderr, "ambiguous") || !strings.Contains(stderr, "asset-13") {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}

func TestRuntimeJSONIsMachineReadableAndOnlyOnStdout(t *testing.T) {
	svc := fakeRuntimeService{list: func(string) ([]runtimeRecord, error) {
		return []runtimeRecord{{Release: "b1", Backend: "cpu", AssetID: "llama-b1-bin-win-cpu-x64.zip"}}, nil
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "list", "--json"}, svc)
	var got struct {
		SchemaVersion int             `json:"schemaVersion"`
		Result        []runtimeRecord `json:"result"`
	}
	if err := json.Unmarshal([]byte(out), &got); code != 0 || err != nil || stderr != "" || got.SchemaVersion != 1 || len(got.Result) != 1 {
		t.Fatalf("got code=%d jsonerr=%v stderr=%q", code, err, stderr)
	}
}

func TestRuntimeRemovePassesExactID(t *testing.T) {
	svc := fakeRuntimeService{remove: func(id string) (runtimeMutation, error) {
		return runtimeMutation{Runtime: runtimeRecord{AssetID: id}, Changed: true}, nil
	}}
	code, out, stderr := runtimeExecute([]string{"runtime", "remove", "llama-b1-bin-win-cpu-x64.zip"}, svc)
	if code != 0 || stderr != "" || !strings.HasPrefix(out, "Removed ") {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}

func TestRuntimeInvalidGrammarPrintsUsageOnStderrOnly(t *testing.T) {
	code, out, stderr := runtimeExecute([]string{"runtime", "download"}, fakeRuntimeService{})
	if code == 0 || out != "" || !strings.Contains(stderr, "coquena runtime download") {
		t.Fatalf("got (%d,%q,%q)", code, out, stderr)
	}
}
