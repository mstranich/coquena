package presets

import (
	"fmt"
	"os"
	"path/filepath"
)

// DefaultRenderPath returns the default location for a written rendering:
// data/servers/<runtimeID>.ini beneath the application home. Rendering never
// writes here on its own; a caller opts in explicitly.
func DefaultRenderPath(home, runtimeID string) string {
	return filepath.Join(home, "data", "servers", runtimeID+".ini")
}

// WriteRendering writes text to path, creating its parent directory if
// needed. It is the only function in this package that touches the
// filesystem for a rendering; Render and Format never do.
func WriteRendering(path, text string) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return fmt.Errorf("create directory for rendered preset %q: %w", path, err)
	}
	if err := os.WriteFile(path, []byte(text), 0o644); err != nil {
		return fmt.Errorf("write rendered preset %q: %w", path, err)
	}
	return nil
}
