package fields

import "testing"

func TestFieldValidateKinds(t *testing.T) {
	for _, kind := range []Kind{KindString, KindInteger, KindBoolean, KindVoid} {
		f := Field{Name: "ctx-size", Kind: kind}
		if err := f.Validate(); err != nil {
			t.Errorf("kind %q: unexpected error: %v", kind, err)
		}
	}
	f := Field{Name: "sampler", Kind: KindEnum, EnumOptions: []string{"top-k", "top-p"}}
	if err := f.Validate(); err != nil {
		t.Errorf("enum: unexpected error: %v", err)
	}
}

func TestFieldValidateRejectsMissingKind(t *testing.T) {
	f := Field{Name: "ctx-size"}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for missing kind")
	}
}

func TestFieldValidateRejectsUnknownKind(t *testing.T) {
	f := Field{Name: "ctx-size", Kind: "number"}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for unknown kind")
	}
}

func TestFieldValidateRejectsInvalidNames(t *testing.T) {
	cases := []string{"--ctx-size", "ctx size", "ctx=size", ""}
	for _, name := range cases {
		f := Field{Name: name, Kind: KindString}
		if err := f.Validate(); err == nil {
			t.Errorf("name %q: expected error", name)
		}
	}
}

func TestFieldValidateRejectsEmptyEnum(t *testing.T) {
	f := Field{Name: "sampler", Kind: KindEnum}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for empty enum options")
	}
}

func TestFieldValidateRejectsDuplicateEnumOptions(t *testing.T) {
	f := Field{Name: "sampler", Kind: KindEnum, EnumOptions: []string{"a", "a"}}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for duplicate enum options")
	}
}

func TestFieldValidateRejectsUnsupportedProvider(t *testing.T) {
	f := Field{Name: "ctx-size", Kind: KindInteger, Availability: &Availability{Provider: "dspark"}}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for unsupported provider")
	}
}

func TestFieldValidateRejectsInvalidReleaseTags(t *testing.T) {
	f := Field{Name: "ctx-size", Kind: KindInteger, Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "10107"}}
	if err := f.Validate(); err == nil {
		t.Fatal("expected error for malformed introducedIn tag")
	}
}

func TestValidateCatalogRejectsDuplicateName(t *testing.T) {
	fs := []Field{
		{Name: "ctx-size", Kind: KindInteger},
		{Name: "ctx-size", Kind: KindString},
	}
	if err := ValidateCatalog(fs); err == nil {
		t.Fatal("expected error for duplicate name")
	}
}

func TestValidateCatalogRejectsAliasCollisionWithName(t *testing.T) {
	fs := []Field{
		{Name: "ctx-size", Kind: KindInteger},
		{Name: "context", Kind: KindInteger, ShortArgument: "ctx-size"},
	}
	if err := ValidateCatalog(fs); err == nil {
		t.Fatal("expected error for alias colliding with another field's name")
	}
}

func TestValidateCatalogRejectsAliasCollisionBetweenAliases(t *testing.T) {
	fs := []Field{
		{Name: "a", Kind: KindInteger, EnvVar: "LLAMA_ARG_A"},
		{Name: "b", Kind: KindInteger, EnvVar: "LLAMA_ARG_A"},
	}
	if err := ValidateCatalog(fs); err == nil {
		t.Fatal("expected error for colliding aliases")
	}
}

func TestValidateCatalogAcceptsUniqueAliases(t *testing.T) {
	fs := []Field{
		{Name: "ctx-size", Kind: KindInteger, ShortArgument: "c", EnvVar: "LLAMA_ARG_CTX_SIZE"},
		{Name: "temp", Kind: KindString},
	}
	if err := ValidateCatalog(fs); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
}

func TestValidateCatalogRejectsSelfParent(t *testing.T) {
	fs := []Field{{Name: "a", Kind: KindVoid, Parent: "a"}}
	if err := ValidateCatalog(fs); err == nil {
		t.Fatal("expected error for self parent")
	}
}

func TestValidateCatalogRejectsUnresolvedParent(t *testing.T) {
	fs := []Field{{Name: "a", Kind: KindVoid, Parent: "missing"}}
	if err := ValidateCatalog(fs); err == nil {
		t.Fatal("expected error for unresolved parent")
	}
}

func TestValidateCatalogAcceptsResolvedParent(t *testing.T) {
	fs := []Field{
		{Name: "parent", Kind: KindVoid},
		{Name: "child", Kind: KindString, Parent: "parent"},
	}
	if err := ValidateCatalog(fs); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
}
