package model

import "fmt"

// CapabilityKind names the signal in a model's own header that implies a
// minimum runtime capability.
type CapabilityKind string

const (
	// CapabilityQuantization reports a declared general.file_type outside the
	// range ordinary artifacts use, so the build must support that
	// quantization scheme to load the file at all.
	CapabilityQuantization CapabilityKind = "quantization"
	// CapabilityArchitecture reports the declared general.architecture the
	// build must know.
	CapabilityArchitecture CapabilityKind = "architecture"
	// CapabilityHybridSSM reports the presence of <arch>.ssm.* keys, so the
	// build must support hybrid attention/SSM inference.
	CapabilityHybridSSM CapabilityKind = "hybrid-ssm"
)

// CapabilityRequirement is one capability a runtime must have to load a model.
//
// It is deliberately not a release bound. Nothing Coquena reads establishes
// which llama.cpp release first supported ternary quantization or hybrid
// attention/SSM inference, so inventing a b<digits> lower bound would be a
// fabricated fact. The requirement therefore states the capability itself and
// leaves matching it to a build to the operator.
type CapabilityRequirement struct {
	Kind   CapabilityKind `json:"kind"`
	Value  string         `json:"value"`
	Detail string         `json:"detail,omitempty"`
}

// RuntimeCapability is the advisory runtime capability a model's own metadata
// implies.
//
// ADVISORY, BY SPECIFICATION. Coquena SHALL NOT infer llama.cpp compatibility
// in order to assign values: this contribution is reported to the operator and
// nothing else. It never assigns a preset value, never selects or launches a
// runtime, and never blocks authoring, storing, renaming or removing a preset.
// The line the change draws is between inferring compatibility to *assign*
// values, which stays prohibited, and computing a constraint *displayed* to the
// operator, which is what a control plane for several runtimes needs in order
// to answer whether an installed build can load a given model.
type RuntimeCapability struct {
	Requirements []CapabilityRequirement `json:"requirements,omitempty"`
}

// Empty reports whether the model implied no capability requirement at all,
// which is the ordinary case: a mainstream quantization in a mainstream
// architecture constrains nothing any current build lacks.
func (r RuntimeCapability) Empty() bool { return len(r.Requirements) == 0 }

// capabilityFileTypes is an explicit recipe-domain set. A numeric range would
// silently assume llama_ftype values are ordered by novelty and previously hid
// ternary recipes whose identifiers fall below that threshold.
var capabilityFileTypes = map[LlamaFileType]struct{}{
	36: {}, // TQ1_0
	37: {}, // TQ2_0
	38: {}, // MXFP4_MOE
	39: {}, // NVFP4
	40: {}, // Q1_0
	41: {}, // Q2_0
}

// DeriveRuntimeCapability derives the advisory runtime capability a
// classification implies, from the three independent signals Bonsai-27B proved
// are readable straight from a header: an out-of-range general.file_type, the
// presence of <arch>.ssm.* keys, and the declared general.architecture.
//
// The architecture alone never triggers a contribution. Every build knows the
// mainstream architectures, so reporting one for an ordinary model would be
// noise rather than a constraint; the architecture is carried only alongside a
// triggering signal, where it is the fact that tells the operator which build
// support to look for. An ordinary model therefore yields an empty capability.
//
// The result is advisory: see RuntimeCapability.
func DeriveRuntimeCapability(c Classification) RuntimeCapability {
	var out RuntimeCapability
	if c.FileType != nil {
		_, requires := capabilityFileTypes[*c.FileType]
		if requires {
			out.Requirements = append(out.Requirements, CapabilityRequirement{
				Kind:   CapabilityQuantization,
				Value:  fmt.Sprintf("%d", *c.FileType),
				Detail: "the build must support this quantization file type",
			})
		}
	}
	if c.SSM {
		out.Requirements = append(out.Requirements, CapabilityRequirement{
			Kind:   CapabilityHybridSSM,
			Value:  "ssm",
			Detail: "the build must support hybrid attention/SSM inference",
		})
	}
	if out.Empty() || c.Architecture == "" {
		return out
	}
	out.Requirements = append(out.Requirements, CapabilityRequirement{
		Kind:   CapabilityArchitecture,
		Value:  c.Architecture,
		Detail: "the build must know this architecture",
	})
	return out
}
