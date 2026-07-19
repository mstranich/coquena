package llamacpp

import (
	"encoding/base64"
	"errors"
	"strings"
	"unicode"
)

func ValidateAssetID(id string) error {
	if id == "" || len(id) > 255 {
		return errors.New("asset ID must contain 1 to 255 characters")
	}
	if id == "." || id == ".." || strings.ContainsAny(id, `/\`) {
		return errors.New("asset ID must be a plain filename")
	}
	if strings.TrimSpace(id) != id {
		return errors.New("asset ID must not contain surrounding whitespace")
	}
	for _, r := range id {
		if unicode.IsControl(r) || r == ':' {
			return errors.New("asset ID contains an unsafe character")
		}
	}
	return nil
}

// EncodeAssetID returns a reversible, collision-free filename safe on supported platforms.
func EncodeAssetID(id string) (string, error) {
	if err := ValidateAssetID(id); err != nil {
		return "", err
	}
	return "asset-" + base64.RawURLEncoding.EncodeToString([]byte(id)), nil
}

func DecodeAssetID(encoded string) (string, error) {
	if !strings.HasPrefix(encoded, "asset-") {
		return "", errors.New("invalid encoded asset ID")
	}
	b, err := base64.RawURLEncoding.DecodeString(strings.TrimPrefix(encoded, "asset-"))
	if err != nil {
		return "", errors.New("invalid encoded asset ID")
	}
	id := string(b)
	if err := ValidateAssetID(id); err != nil {
		return "", err
	}
	return id, nil
}
