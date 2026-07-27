package model

import (
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"strings"
)

// ArtifactType is the artifact role declared by the file itself through
// general.type. It is intrinsic to the file, unlike the role an operator
// assigns to it in a preset: a speculative draft model declares TypeModel and
// nothing in its header says otherwise.
type ArtifactType string

const (
	// TypeModel is a principal model artifact (general.type = "model").
	TypeModel ArtifactType = "model"
	// TypeProjector is a multimodal projector (general.type = "mmproj").
	TypeProjector ArtifactType = "mmproj"
	// TypeUnknown is reported when general.type is absent or carries a value
	// Coquena does not recognise. It is never inferred from the artifact's
	// architecture or filename, because neither is a reliable proxy: a
	// projector reports architecture "clip" and a Bonsai principal reports
	// "qwen35" rather than its own name.
	TypeUnknown ArtifactType = "unknown"
)

// TokenizerDigest identifies an artifact's tokenizer compactly enough to be
// persisted and compared. Digest replaces the vocabulary arrays themselves —
// on a real artifact they account for 99.9% of the metadata — while remaining
// exactly the evidence the draft-to-target criterion needs, since comparing
// two digests is an exact comparison of the whole vocabulary.
type TokenizerDigest struct {
	Model     string `json:"model,omitempty"`
	Pre       string `json:"pre,omitempty"`
	VocabSize int64  `json:"vocabSize,omitempty"`
	BOS       *int64 `json:"bos,omitempty"`
	EOS       *int64 `json:"eos,omitempty"`
	Pad       *int64 `json:"pad,omitempty"`
	Unk       *int64 `json:"unk,omitempty"`
	Digest    string `json:"digest,omitempty"`
}

// Sampling carries the publisher's recommended sampling values. They are
// advisory: Coquena displays them attributed to the publisher and never
// assigns them to a preset field.
type Sampling struct {
	Temp *float64 `json:"temp,omitempty"`
	TopK *int64   `json:"topK,omitempty"`
	TopP *float64 `json:"topP,omitempty"`
}

// Classification is the compact digest derived from a GGUF header. It holds
// every fact needed to identify, classify and constrain an artifact — around
// 150 bytes on a real file — and never retains the vocabulary arrays.
// Principal-only fields (ContextLength, EmbeddingLength) and projector-only
// fields (ProjectionDim, the encoder flags, ProjectorType) are each populated
// only when the artifact actually declares them.
type Classification struct {
	Type             ArtifactType    `json:"type"`
	Architecture     string          `json:"architecture,omitempty"`
	SizeLabel        string          `json:"sizeLabel,omitempty"`
	FileType         *int64          `json:"fileType,omitempty"`
	ContextLength    int64           `json:"contextLength,omitempty"`
	EmbeddingLength  int64           `json:"embeddingLength,omitempty"`
	ProjectionDim    int64           `json:"projectionDim,omitempty"`
	ProjectorType    string          `json:"projectorType,omitempty"`
	HasVisionEncoder bool            `json:"hasVisionEncoder,omitempty"`
	HasAudioEncoder  bool            `json:"hasAudioEncoder,omitempty"`
	SSM              bool            `json:"ssm"`
	Tokenizer        TokenizerDigest `json:"tokenizer"`
	Sampling         Sampling        `json:"sampling"`
}

// Classify derives an artifact's classification digest from its decoded GGUF
// metadata map. It reads architecture-namespaced keys through the artifact's
// own general.architecture value rather than a fixed prefix, so gemma4 and
// qwen35 artifacts are both understood, and it tolerates the known projector
// key spellings without guessing at an unrecognised one.
func Classify(meta map[string]any) Classification {
	arch := metaString(meta, "general.architecture")
	c := Classification{
		Type:         declaredType(meta),
		Architecture: arch,
		SizeLabel:    metaString(meta, "general.size_label"),
		FileType:     metaOptionalInt(meta, "general.file_type"),
		Tokenizer:    tokenizerDigest(meta),
		Sampling:     samplingRecommendations(meta),
	}
	if arch != "" {
		c.ContextLength, _ = metaInt(meta, arch+".context_length")
		c.EmbeddingLength, _ = metaInt(meta, arch+".embedding_length")
		c.SSM = hasPrefixedKey(meta, arch+".ssm.")
	}
	c.ProjectionDim, c.ProjectorType, c.HasVisionEncoder, c.HasAudioEncoder = projectorFacts(meta)
	return c
}

// declaredType reads general.type only. An absent or unrecognised value is
// reported as TypeUnknown so an unclassifiable artifact degrades to an
// explicit unknown rather than a wrong classification.
func declaredType(meta map[string]any) ArtifactType {
	switch metaString(meta, "general.type") {
	case string(TypeModel):
		return TypeModel
	case string(TypeProjector):
		return TypeProjector
	default:
		return TypeUnknown
	}
}

// projectorFacts reads the projector keys, accepting both spellings observed
// in the wild: gemma-4 exposes clip.vision.projector_type while Bonsai omits
// the "vision" infix. The linking value is the projection dimension, never
// clip.vision.embedding_length, which is the vision encoder's internal width
// and matches nothing in a principal model.
func projectorFacts(meta map[string]any) (dim int64, projector string, vision, audio bool) {
	for _, key := range []string{"clip.vision.projection_dim", "clip.projection_dim"} {
		if v, ok := metaInt(meta, key); ok {
			dim = v
			break
		}
	}
	for _, key := range []string{"clip.vision.projector_type", "clip.projector_type"} {
		if v := metaString(meta, key); v != "" {
			projector = v
			break
		}
	}
	vision, _ = metaBool(meta, "clip.has_vision_encoder")
	audio, _ = metaBool(meta, "clip.has_audio_encoder")
	return dim, projector, vision, audio
}

func tokenizerDigest(meta map[string]any) TokenizerDigest {
	d := TokenizerDigest{
		Model:  metaString(meta, "tokenizer.ggml.model"),
		Pre:    metaString(meta, "tokenizer.ggml.pre"),
		BOS:    metaOptionalInt(meta, "tokenizer.ggml.bos_token_id"),
		EOS:    metaOptionalInt(meta, "tokenizer.ggml.eos_token_id"),
		Pad:    metaOptionalInt(meta, "tokenizer.ggml.padding_token_id"),
		Unk:    metaOptionalInt(meta, "tokenizer.ggml.unknown_token_id"),
		Digest: vocabularyDigest(meta),
	}
	if tokens, ok := meta["tokenizer.ggml.tokens"].([]any); ok {
		d.VocabSize = int64(len(tokens))
	}
	return d
}

// vocabularyDigest hashes the vocabulary arrays into a fixed-size value so
// two artifacts sharing a vocabulary produce an identical digest without
// either of them retaining the arrays. Each section is length-prefixed and
// tagged by name so distinct arrays can never collide by concatenation.
func vocabularyDigest(meta map[string]any) string {
	h := sha256.New()
	present := false
	for _, key := range []string{"tokenizer.ggml.tokens", "tokenizer.ggml.merges", "tokenizer.ggml.token_type"} {
		values, ok := meta[key].([]any)
		if !ok {
			continue
		}
		present = true
		writeDigestChunk(h, key)
		var count [8]byte
		binary.LittleEndian.PutUint64(count[:], uint64(len(values)))
		_, _ = h.Write(count[:])
		for _, value := range values {
			writeDigestChunk(h, digestToken(value))
		}
	}
	if !present {
		return ""
	}
	return "sha256:" + hex.EncodeToString(h.Sum(nil))
}

func writeDigestChunk(h interface{ Write([]byte) (int, error) }, s string) {
	var n [8]byte
	binary.LittleEndian.PutUint64(n[:], uint64(len(s)))
	_, _ = h.Write(n[:])
	_, _ = h.Write([]byte(s))
}

// digestToken renders one vocabulary element as text so that a token type
// stored as int32 by one publisher and uint32 by another still hashes alike.
func digestToken(value any) string {
	if s, ok := value.(string); ok {
		return s
	}
	if n, ok := toInt(value); ok {
		return fmt.Sprintf("%d", n)
	}
	return fmt.Sprintf("%v", value)
}

func samplingRecommendations(meta map[string]any) Sampling {
	return Sampling{
		Temp: metaOptionalFloat(meta, "general.sampling.temp"),
		TopK: metaOptionalInt(meta, "general.sampling.top_k"),
		TopP: metaOptionalFloat(meta, "general.sampling.top_p"),
	}
}

func hasPrefixedKey(meta map[string]any, prefix string) bool {
	for key := range meta {
		if strings.HasPrefix(key, prefix) {
			return true
		}
	}
	return false
}

func metaString(meta map[string]any, key string) string {
	s, _ := meta[key].(string)
	return s
}
func metaBool(meta map[string]any, key string) (bool, bool) {
	b, ok := meta[key].(bool)
	return b, ok
}
func metaInt(meta map[string]any, key string) (int64, bool) {
	value, ok := meta[key]
	if !ok {
		return 0, false
	}
	return toInt(value)
}
func metaOptionalInt(meta map[string]any, key string) *int64 {
	if v, ok := metaInt(meta, key); ok {
		return &v
	}
	return nil
}
func metaOptionalFloat(meta map[string]any, key string) *float64 {
	value, ok := meta[key]
	if !ok {
		return nil
	}
	if f, ok := toFloat(value); ok {
		return &f
	}
	return nil
}

// toInt normalises the several integer widths ReadMetadata can produce for a
// single logical value, since GGUF publishers disagree on the width they use
// for the same key.
func toInt(value any) (int64, bool) {
	switch v := value.(type) {
	case uint8:
		return int64(v), true
	case int8:
		return int64(v), true
	case uint16:
		return int64(v), true
	case int16:
		return int64(v), true
	case uint32:
		return int64(v), true
	case int32:
		return int64(v), true
	case uint64:
		return int64(v), true
	case int64:
		return v, true
	case int:
		return int64(v), true
	case float64:
		return int64(v), true
	case float32:
		return int64(v), true
	default:
		return 0, false
	}
}
func toFloat(value any) (float64, bool) {
	switch v := value.(type) {
	case float32:
		return float64(v), true
	case float64:
		return v, true
	default:
		if n, ok := toInt(value); ok {
			return float64(n), true
		}
		return 0, false
	}
}
