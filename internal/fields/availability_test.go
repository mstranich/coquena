package fields

import "testing"

func TestEvaluateInRange(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000", RemovedIn: "b10200"}}
	if got := Evaluate(f, ProviderOfficial, "b10100"); got != Admitted {
		t.Fatalf("got %q, want admitted", got)
	}
}

func TestEvaluateBelowRange(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000"}}
	if got := Evaluate(f, ProviderOfficial, "b9999"); got != NotAdmitted {
		t.Fatalf("got %q, want not-admitted", got)
	}
}

func TestEvaluateAtExclusion(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000", RemovedIn: "b10200"}}
	if got := Evaluate(f, ProviderOfficial, "b10200"); got != NotAdmitted {
		t.Fatalf("got %q, want not-admitted at exclusion boundary", got)
	}
}

func TestEvaluateAfterExclusion(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000", RemovedIn: "b10200"}}
	if got := Evaluate(f, ProviderOfficial, "b10300"); got != NotAdmitted {
		t.Fatalf("got %q, want not-admitted", got)
	}
}

func TestEvaluateUnbounded(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial}}
	if got := Evaluate(f, ProviderOfficial, "b10100"); got != Unknown {
		t.Fatalf("got %q, want unknown for unbounded field", got)
	}
}

func TestEvaluateNoAvailabilityDeclared(t *testing.T) {
	f := Field{}
	if got := Evaluate(f, ProviderOfficial, "b10100"); got != Unknown {
		t.Fatalf("got %q, want unknown", got)
	}
}

func TestEvaluateUnorderableRelease(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000"}}
	if got := Evaluate(f, ProviderOfficial, "not-a-tag"); got != Unknown {
		t.Fatalf("got %q, want unknown for unorderable release", got)
	}
}

func TestEvaluateDifferentProvider(t *testing.T) {
	f := Field{Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b10000"}}
	if got := Evaluate(f, "dspark", "b10100"); got != Unknown {
		t.Fatalf("got %q, want unknown for mismatched provider", got)
	}
}

func TestEvaluateNeverBlocksMutation(t *testing.T) {
	c := Catalog{SchemaVersion: SchemaVersion, Fields: []Field{
		{Name: "ctx-size", Kind: KindInteger, Availability: &Availability{Provider: ProviderOfficial, IntroducedIn: "b99999"}},
	}}
	// A not-admitted or unknown field is still a fully valid catalog entry.
	if err := ValidateCatalog(c.Fields); err != nil {
		t.Fatalf("unexpected validation error for a not-admitted field: %v", err)
	}
}
