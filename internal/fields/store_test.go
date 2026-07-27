package fields

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func newTestStore(t *testing.T) *Store {
	t.Helper()
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	return NewStore(home)
}

func TestLoadEmptyInstallation(t *testing.T) {
	s := newTestStore(t)
	c, err := s.Load()
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(c.Fields) != 0 {
		t.Fatalf("expected empty catalog, got %d fields", len(c.Fields))
	}
	if _, err := os.Stat(s.path()); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("expected no catalog document to be created by Load")
	}
}

func TestDefinePublishesFirstDocument(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger}); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	data, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatalf("expected catalog document to exist: %v", err)
	}
	if data[len(data)-1] != '\n' {
		t.Fatal("expected catalog document to end with a newline")
	}
}

func TestDefineSurvivesRestart(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger}); err != nil {
		t.Fatal(err)
	}
	reloaded := NewStore(s.home)
	c, err := reloaded.Load()
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := c.Find("ctx-size"); !ok {
		t.Fatal("expected field to survive a fresh Store instance")
	}
}

func TestDefineRejectsExistingName(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindString}); err == nil {
		t.Fatal("expected error defining an existing name")
	}
}

func TestUpsertCreatesAndReplaces(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Upsert(Field{Name: "ctx-size", Kind: KindInteger, Default: 4096}); err != nil {
		t.Fatal(err)
	}
	c, err := s.Upsert(Field{Name: "ctx-size", Kind: KindInteger, Default: 8192})
	if err != nil {
		t.Fatal(err)
	}
	f, ok := c.Find("ctx-size")
	if !ok {
		t.Fatal("expected field to be present")
	}
	if f.Default != 8192 {
		t.Fatalf("expected default 8192, got %v", f.Default)
	}
}

func TestReplayScriptIsIdempotent(t *testing.T) {
	s := newTestStore(t)
	apply := func() Catalog {
		c, err := s.Upsert(Field{Name: "ctx-size", Kind: KindInteger, Default: 4096})
		if err != nil {
			t.Fatal(err)
		}
		c, err = s.Upsert(Field{Name: "temp", Kind: KindString, Default: "0.8"})
		if err != nil {
			t.Fatal(err)
		}
		return c
	}
	first := apply()
	second := apply()
	if len(first.Fields) != len(second.Fields) {
		t.Fatalf("expected replay to be idempotent: %d vs %d fields", len(first.Fields), len(second.Fields))
	}
}

func TestRemoveDeletesSubtree(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "parent", Kind: KindVoid}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Field{Name: "child", Kind: KindString, Parent: "parent"}); err != nil {
		t.Fatal(err)
	}
	c, err := s.Remove("parent")
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := c.Find("parent"); ok {
		t.Fatal("expected parent to be removed")
	}
	if _, ok := c.Find("child"); ok {
		t.Fatal("expected child subtree to be removed")
	}
}

func TestRemoveResolvesByAlias(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger, ShortArgument: "c", EnvVar: "LLAMA_ARG_CTX_SIZE"}); err != nil {
		t.Fatal(err)
	}
	c, err := s.Remove("c")
	if err != nil {
		t.Fatalf("unexpected error removing by short-argument alias: %v", err)
	}
	if _, ok := c.Find("ctx-size"); ok {
		t.Fatal("expected field to be removed when addressed by alias")
	}
}

func TestRemoveUnknownField(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Remove("missing"); err == nil {
		t.Fatal("expected error removing an unknown field")
	}
}

func TestLoadRejectsMalformedDocument(t *testing.T) {
	s := newTestStore(t)
	if err := os.WriteFile(s.path(), []byte("not json"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Load(); err == nil {
		t.Fatal("expected error for malformed catalog document")
	}
}

func TestLoadRejectsUnsupportedSchemaVersion(t *testing.T) {
	s := newTestStore(t)
	if err := os.WriteFile(s.path(), []byte(`{"schemaVersion":99,"fields":[]}`), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Load(); err == nil {
		t.Fatal("expected error for unsupported schema version")
	}
}

func TestDefineRejectsAndLeavesPreviousDocumentIntact(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger}); err != nil {
		t.Fatal(err)
	}
	before, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Field{Name: "--bad", Kind: KindInteger}); err == nil {
		t.Fatal("expected validation error for invalid name")
	}
	after, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("expected previous catalog document to remain unchanged after a rejected mutation")
	}
}

func TestPublishFailureLeavesPreviousDocumentIntact(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Field{Name: "ctx-size", Kind: KindInteger}); err != nil {
		t.Fatal(err)
	}
	before, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	failing := s.ops
	failing.rename = func(string, string) error { return errors.New("simulated rename failure") }
	s.ops = failing
	if _, err := s.Define(Field{Name: "temp", Kind: KindString}); err == nil {
		t.Fatal("expected publish failure to surface an error")
	}
	after, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("expected previous catalog document to remain unchanged after a publish failure")
	}
}
