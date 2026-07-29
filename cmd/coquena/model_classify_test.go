package main

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// cliGGUF builds a minimal GGUF header carrying the given key/value pairs, the
// same shape the model package's own fixtures use. Values are a string or a
// uint32, which covers every key the classification digest reads here.
func cliGGUF(pairs ...any) []byte {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[16:], uint64(len(pairs)/2))
	for i := 0; i < len(pairs); i += 2 {
		key := pairs[i].(string)
		var n [8]byte
		binary.LittleEndian.PutUint64(n[:], uint64(len(key)))
		b = append(append(b, n[:]...), key...)
		var t [4]byte
		switch value := pairs[i+1].(type) {
		case string:
			binary.LittleEndian.PutUint32(t[:], 8)
			b = append(b, t[:]...)
			binary.LittleEndian.PutUint64(n[:], uint64(len(value)))
			b = append(append(b, n[:]...), value...)
		case uint32:
			binary.LittleEndian.PutUint32(t[:], 4)
			b = append(b, t[:]...)
			var v [4]byte
			binary.LittleEndian.PutUint32(v[:], value)
			b = append(b, v[:]...)
		}
	}
	return b
}

func cliPrincipalGGUF() []byte {
	return cliGGUF("general.type", "model", "general.architecture", "qwen35", "general.file_type", uint32(15), "qwen35.embedding_length", uint32(5120))
}

func cliProjectorGGUF() []byte {
	return cliGGUF("general.type", "mmproj", "general.architecture", "clip", "general.file_type", uint32(15), "clip.vision.projection_dim", uint32(5120))
}

// repositoryServer serves the Hugging Face surface discovery and download read:
// the revision listing, the model card, and each artifact by HEAD, byte range,
// and whole-body GET.
func repositoryServer(t *testing.T, files map[string][]byte, card string) *httptest.Server {
	t.Helper()
	names := make([]string, 0, len(files))
	for name := range files {
		names = append(names, name)
	}
	sort.Strings(names)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/models/org/repo/revision/main" {
			siblings := make([]string, 0, len(names))
			for _, name := range names {
				siblings = append(siblings, fmt.Sprintf(`{"rfilename":%q,"size":%d}`, name, len(files[name])))
			}
			_, _ = fmt.Fprintf(w, `{"sha":"abc","siblings":[%s]}`, strings.Join(siblings, ","))
			return
		}
		if r.URL.Path == "/org/repo/resolve/abc/README.md" {
			if card == "" {
				http.NotFound(w, r)
				return
			}
			_, _ = io.WriteString(w, card)
			return
		}
		body, ok := files[strings.TrimPrefix(r.URL.Path, "/org/repo/resolve/abc/")]
		if !ok {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Length", strconv.Itoa(len(body)))
		if r.Method == http.MethodHead {
			return
		}
		if header := r.Header.Get("Range"); header != "" {
			var start, end int
			if _, err := fmt.Sscanf(strings.TrimPrefix(header, "bytes="), "%d-%d", &start, &end); err != nil {
				t.Fatal(err)
			}
			w.Header().Set("Content-Length", strconv.Itoa(end-start+1))
			w.WriteHeader(http.StatusPartialContent)
			_, _ = w.Write(body[start : end+1])
			return
		}
		_, _ = w.Write(body)
	}))
	t.Cleanup(server.Close)
	return server
}

func withRepositoryManager(t *testing.T, files map[string][]byte, card string) *modelapp.Manager {
	t.Helper()
	server := repositoryServer(t, files, card)
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	m.Client = server.Client()
	m.Resolver = modelapp.Resolver{Client: server.Client(), BaseURL: server.URL}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	return m
}

// Discovery reports every candidate with its classification and marks the one
// the selector resolves to, rather than reducing the list to that candidate.
func TestRunModelDiscoverReportsEveryCandidateAndMarksSelector(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{
		"principal-Q4_K_M.gguf": cliPrincipalGGUF(),
		"mmproj-BF16.gguf":      cliProjectorGGUF(),
	}, "---\nlicense: mit\n---\n# Bonsai\nA principal and its projector.\n")

	var human bytes.Buffer
	if err := runModel([]string{"discover", "org/repo:Q4_K_M"}, &human); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"principal-Q4_K_M.gguf", "mmproj-BF16.gguf", "model", "mmproj"} {
		if !strings.Contains(human.String(), expected) {
			t.Fatalf("missing %q in %s", expected, human.String())
		}
	}
	// The candidate list is what discovery exists to present, so the model card
	// must not appear and bury it — in either output mode.
	for _, forbidden := range []string{"Model card:", "A principal and its projector.", "license: mit"} {
		if strings.Contains(human.String(), forbidden) {
			t.Fatalf("model card leaked into discovery output (%q): %s", forbidden, human.String())
		}
	}

	var jsonOut bytes.Buffer
	if err := runModel([]string{"discover", "org/repo:Q4_K_M", "--json"}, &jsonOut); err != nil {
		t.Fatal(err)
	}
	var envelope struct {
		SchemaVersion int                `json:"schemaVersion"`
		Result        modelapp.Discovery `json:"result"`
	}
	if err := json.Unmarshal(jsonOut.Bytes(), &envelope); err != nil {
		t.Fatal(err)
	}
	if envelope.SchemaVersion != 1 || len(envelope.Result.Candidates) != 2 {
		t.Fatalf("envelope=%+v", envelope)
	}
	selected := 0
	for _, candidate := range envelope.Result.Candidates {
		if candidate.Selected {
			selected++
			if candidate.Source.Filename != "principal-Q4_K_M.gguf" {
				t.Fatalf("selector marked %q", candidate.Source.Filename)
			}
		}
	}
	if selected != 1 {
		t.Fatalf("selected=%d", selected)
	}
	if envelope.Result.ModelCard != "" {
		t.Fatalf("model card reported: %q", envelope.Result.ModelCard)
	}
}

// Whether or not a repository publishes a model card, discovery reports its
// candidates and nothing else: the card's presence must make no difference to
// the output now that reporting it is turned off.
func TestRunModelDiscoverReportsCandidatesWithoutTheModelCard(t *testing.T) {
	for _, tc := range []struct {
		name string
		card string
	}{
		{"with a model card", "---\nlicense: mit\n---\n# Bonsai\nA principal and its projector.\n"},
		{"without a model card", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			withRepositoryManager(t, map[string][]byte{"principal.gguf": cliPrincipalGGUF()}, tc.card)
			var out bytes.Buffer
			if err := runModel([]string{"discover", "org/repo"}, &out); err != nil {
				t.Fatal(err)
			}
			if !strings.Contains(out.String(), "principal.gguf") {
				t.Fatalf("candidate missing: %s", out.String())
			}
			for _, forbidden := range []string{"Model card:", "A principal and its projector."} {
				if strings.Contains(out.String(), forbidden) {
					t.Fatalf("model card leaked (%q): %s", forbidden, out.String())
				}
			}
		})
	}
}

// refusingTransport fails any request, proving inspect never issues one.
type refusingTransport struct{ t *testing.T }

func (r refusingTransport) RoundTrip(*http.Request) (*http.Response, error) {
	r.t.Fatal("inspect issued a network request")
	return nil, nil
}

// inspect operates on a local path and issues no network request; its human
// output is the Metadata/Value table carrying the declared type.
func TestRunModelInspectLocalRendersClassifiedTable(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	m.Client = &http.Client{Transport: refusingTransport{t}}

	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/m.gguf"}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source}); err != nil {
		t.Fatal(err)
	}
	// Named by its logical reference, not by a filesystem path.
	var human bytes.Buffer
	if err := runModel([]string{"inspect", "org/repo"}, &human); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"METADATA", "VALUE", "classification.type", "model", "general.architecture", "qwen35"} {
		if !strings.Contains(human.String(), expected) {
			t.Fatalf("missing %q in %s", expected, human.String())
		}
	}
	// Deterministic across runs: the same artifact renders identically.
	var again bytes.Buffer
	if err := runModel([]string{"inspect", "org/repo"}, &again); err != nil {
		t.Fatal(err)
	}
	if again.String() != human.String() {
		t.Fatalf("non-deterministic output:\n%s\n%s", human.String(), again.String())
	}
}

// A selector matching several artifacts must name them, not only count them.
func TestRunModelNamesCandidatesOnAmbiguousSelector(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{
		"model-Q4_K_M.gguf": cliPrincipalGGUF(),
		"model-Q4_K_S.gguf": cliPrincipalGGUF(),
	}, "")
	err := runModel([]string{"resolve", "org/repo:Q4"}, &bytes.Buffer{})
	if err == nil {
		t.Fatal("ambiguous selector accepted")
	}
	for _, expected := range []string{"model-Q4_K_M.gguf", "model-Q4_K_S.gguf", "available GGUF artifacts:"} {
		if !strings.Contains(err.Error(), expected) {
			t.Fatalf("missing %q in %v", expected, err)
		}
	}
}

// publishArtifact publishes one artifact body into the manager's library.
func publishArtifact(t *testing.T, m *modelapp.Manager, filename string, body []byte) {
	t.Helper()
	partial := filepath.Join(t.TempDir(), filename+".partial")
	if err := os.WriteFile(partial, body, 0600); err != nil {
		t.Fatal(err)
	}
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: filename, URL: "https://example.test/" + filename}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source}); err != nil {
		t.Fatal(err)
	}
}

// A principal and a projector compatible with it are two independent rows: the
// listing reports facts, not the many-to-many relation between them.
func TestRunModelListReportsEveryArtifactAtTheSameLevel(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "principal.gguf", cliPrincipalGGUF())
	publishArtifact(t, m, "mmproj.gguf", cliProjectorGGUF())

	var human bytes.Buffer
	if err := runModel([]string{"list"}, &human); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"org/repo:Q8_K", "model", "mmproj"} {
		if !strings.Contains(human.String(), expected) {
			t.Fatalf("missing %q in %s", expected, human.String())
		}
	}
	if strings.Contains(human.String(), "└─") {
		t.Fatalf("listing still draws a tree: %s", human.String())
	}

	var jsonOut bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &jsonOut); err != nil {
		t.Fatal(err)
	}
	var envelope struct {
		Result []modelListEntry `json:"result"`
	}
	if err := json.Unmarshal(jsonOut.Bytes(), &envelope); err != nil {
		t.Fatal(err)
	}
	if len(envelope.Result) != 2 {
		t.Fatalf("expected both artifacts at the top level, result=%+v", envelope.Result)
	}
	types := map[string]string{}
	for _, entry := range envelope.Result {
		types[entry.Filename] = entry.Type
	}
	if types["principal.gguf"] != "model" || types["mmproj.gguf"] != "mmproj" {
		t.Fatalf("types=%+v", types)
	}
	// No nesting survives anywhere in the document, key included.
	if strings.Contains(jsonOut.String(), "companions") {
		t.Fatalf("companions key still emitted: %s", jsonOut.String())
	}
}

// An unreconciled artifact is not addressable by the commands that read the
// catalog: reconciling it first is what keeps one logical name meaning one
// coherent thing.
func TestRunModelInspectRefusesAnUnreconciledArtifact(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	dir := filepath.Join(m.ModelsDir, "publisher", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "foreign.gguf"), cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	err := runModel([]string{"inspect", "publisher/repo"}, &bytes.Buffer{})
	if err == nil {
		t.Fatal("expected a diagnostic for an unreconciled artifact")
	}
	if !strings.Contains(err.Error(), "installed") {
		t.Fatalf("diagnostic does not point at reconciliation: %v", err)
	}
}

// Reconciliation names its subject with the same user/repo:QUANT reference the
// rest of the model commands accept, so an operator never has to translate a
// listed artifact into a filesystem path to act on it.
func TestRunModelReconcileAcceptsAReference(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	foreign := filepath.Join(m.ModelsDir, "publisher", "repo", "foreign.gguf")
	if err := os.MkdirAll(filepath.Dir(foreign), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(foreign, cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	var out bytes.Buffer
	if err := runModel([]string{"reconcile", "publisher/repo", "--accept"}, &out); err != nil {
		t.Fatalf("reconcile by reference: %v", err)
	}

	// The artifact is installed afterward, so the same reference now resolves
	// through the installed path every other command uses.
	entry, err := m.ResolveInstalled("publisher/repo")
	if err != nil {
		t.Fatalf("reconciled artifact is not resolvable: %v", err)
	}
	if entry.State != "installed" {
		t.Fatalf("state=%q", entry.State)
	}
}

// A reference naming no unreconciled artifact reports that state rather than
// claiming the artifact is not installed, which would invert the subject.
func TestRunModelReconcileReportsUnreconciledState(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	err := runModel([]string{"reconcile", "publisher/repo", "--accept"}, &bytes.Buffer{})
	if err == nil {
		t.Fatal("expected a diagnostic")
	}
	if !strings.Contains(err.Error(), "unreconciled") {
		t.Fatalf("diagnostic does not name the state: %v", err)
	}
}

// A projector compatible with no installed principal stays at the top level.
func TestRunModelListReportsUnmatchedCompanion(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "mmproj.gguf", cliProjectorGGUF())

	var jsonOut bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &jsonOut); err != nil {
		t.Fatal(err)
	}
	var envelope struct {
		Result []modelListEntry `json:"result"`
	}
	if err := json.Unmarshal(jsonOut.Bytes(), &envelope); err != nil {
		t.Fatal(err)
	}
	if len(envelope.Result) != 1 || envelope.Result[0].Filename != "mmproj.gguf" || envelope.Result[0].Type != "mmproj" {
		t.Fatalf("result=%+v", envelope.Result)
	}
}

// Library-wide reconciliation is turned off: --all is reported as not accepted
// rather than ignored, and nothing is written for the artifacts it would have
// walked.
func TestRunModelReconcileRefusesAll(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	dir := filepath.Join(m.ModelsDir, "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	for name, body := range map[string][]byte{"principal.gguf": cliPrincipalGGUF(), "mmproj.gguf": cliProjectorGGUF()} {
		if err := os.WriteFile(filepath.Join(dir, name), body, 0600); err != nil {
			t.Fatal(err)
		}
	}

	for _, args := range [][]string{
		{"reconcile", "--all", "--accept"},
		{"reconcile", "--all", "--online"},
		{"reconcile", "--all"},
	} {
		var out bytes.Buffer
		err := runModel(args, &out)
		if err == nil {
			t.Fatalf("%v: expected refusal, out=%s", args, out.String())
		}
		if !strings.Contains(err.Error(), "--all is not accepted") {
			t.Fatalf("%v: err=%v", args, err)
		}
		if out.Len() != 0 {
			t.Fatalf("%v: wrote %q", args, out.String())
		}
	}

	// The refusal writes no sidecar for either artifact.
	for _, name := range []string{"principal.json", "mmproj.json"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatalf("%s: expected no sidecar, err=%v", name, err)
		}
	}
}

// Reconciliation prompts, so it cannot emit exactly one JSON document: --json
// is refused rather than accepted and ignored, in both modes.
func TestRunModelReconcileRefusesJSON(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	dir := filepath.Join(m.ModelsDir, "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "principal.gguf"), cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	for _, mode := range []string{"--accept", "--online"} {
		var out bytes.Buffer
		err := runModel([]string{"reconcile", "org/repo", mode, "--json"}, &out)
		if err == nil {
			t.Fatalf("%s: expected refusal, out=%s", mode, out.String())
		}
		if !strings.Contains(err.Error(), "--json does not apply to this command") {
			t.Fatalf("%s: err=%v", mode, err)
		}
		if out.Len() != 0 {
			t.Fatalf("%s: wrote %q", mode, out.String())
		}
	}
	if _, err := os.Stat(filepath.Join(dir, "principal.json")); !os.IsNotExist(err) {
		t.Fatalf("expected no sidecar, err=%v", err)
	}
}

// The companion flag creates one job per artifact and reports every job ID; its
// absence leaves the single-artifact default untouched.
func TestRunModelDownloadWithCompanionsReportsEveryJobID(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{
		"principal.gguf": cliPrincipalGGUF(),
		"mmproj.gguf":    cliProjectorGGUF(),
	}, "")

	var out bytes.Buffer
	if err := runModel([]string{"download", "org/repo:principal.gguf", "--with-companions"}, &out); err != nil {
		t.Fatal(err)
	}
	ids := strings.Fields(strings.TrimSpace(out.String()))
	if len(ids) != 2 || ids[0] == ids[1] {
		t.Fatalf("expected two distinct job IDs, out=%q", out.String())
	}
}

func TestRunModelDownloadWithoutFlagCreatesOneJob(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{
		"principal.gguf": cliPrincipalGGUF(),
		"mmproj.gguf":    cliProjectorGGUF(),
	}, "")
	var out bytes.Buffer
	if err := runModel([]string{"download", "org/repo:principal.gguf"}, &out); err != nil {
		t.Fatal(err)
	}
	if len(strings.Fields(strings.TrimSpace(out.String()))) != 1 {
		t.Fatalf("expected exactly one job ID, out=%q", out.String())
	}
}

// The nested download job actions must keep dispatching as actions rather than
// being read as a repository reference, with the companion flag now in play.
func TestRunModelDownloadActionsStillDispatch(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runModel([]string{"download", "list", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if out.String() != "{\"schemaVersion\":1,\"result\":[]}\n" {
		t.Fatalf("out=%s", out.String())
	}
}
