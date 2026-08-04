package model

import "testing"

func TestParseModelReferenceGrammar(t *testing.T) {
	tests := []struct {
		name, input, repo, revision, selector string
		explicit                              bool
		kind                                  SelectorKind
		wantErr                               bool
	}{
		{"repository", "user/repo", "user/repo", "", "", false, SelectorAbsent, false},
		{"quant", "user/repo:Q4_K_M", "user/repo", "", "Q4_K_M", false, SelectorQuantization, false},
		{"filename", "user/repo:model.gguf", "user/repo", "", "model.gguf", false, SelectorFilename, false},
		{"pinned quant", "user/repo@abc:Q4_K_M", "user/repo", "abc", "Q4_K_M", true, SelectorQuantization, false},
		{"pinned filename", "user/repo@abc:model.gguf", "user/repo", "abc", "model.gguf", true, SelectorFilename, false},
		{"slash filename", "user/repo/model.gguf", "", "", "", false, "", true},
		{"nested selector", "user/repo:Q4_K_M:model.gguf", "", "", "", false, "", true},
		{"empty selector", "user/repo:", "", "", "", false, "", true},
		{"empty revision", "user/repo@:Q4_K_M", "", "", "", false, "", true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := ParseModelReference(tt.input)
			if (err != nil) != tt.wantErr {
				t.Fatalf("err=%v wantErr=%v", err, tt.wantErr)
			}
			if tt.wantErr {
				return
			}
			if got.Repository != tt.repo || got.Revision != tt.revision || got.RevisionExplicit != tt.explicit ||
				got.Selector != tt.selector || got.SelectorKind != tt.kind {
				t.Fatalf("got=%+v", got)
			}
		})
	}
}

func TestCanonicalReferenceUsesTrustedSourceFields(t *testing.T) {
	tests := []struct {
		name string
		src  Source
		want string
	}{
		{"remote", Source{Provider: "huggingface", Repository: "user/repo", ResolvedRevision: "abc", Filename: "odd-Q5_K_M-imatrix.gguf"}, "user/repo@abc:odd-Q5_K_M-imatrix.gguf"},
		{"local", Source{Provider: "local", Repository: "user/repo", Filename: "odd-Q5_K_M-imatrix.gguf"}, "user/repo:odd-Q5_K_M-imatrix.gguf"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := CanonicalReference(tt.src); got != tt.want {
				t.Fatalf("got=%q want=%q", got, tt.want)
			}
		})
	}
}
