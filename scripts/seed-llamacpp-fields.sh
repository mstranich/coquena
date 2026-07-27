#!/bin/sh
# Seeds a starter llama.cpp field catalog through the `coquena field` CLI.
#
# Safe to re-run: every definition uses --overwrite, so replaying this script
# against an installation that already has these fields converges to the same
# catalog instead of failing on a duplicate name.
#
# Usage:
#   ./scripts/seed-llamacpp-fields.sh
#   COQUENA_BIN=./bin/coquena ./scripts/seed-llamacpp-fields.sh
#
# COQUENA_BIN defaults to "coquena" resolved from PATH. Run `coquena init`
# before this script if the installation has not been initialized yet.
#
# The fields below are the arguments documented in llama.cpp's
# `--models-preset` example (docs/preset.md): they are the arguments a
# `[*]` defaults section or a model section is expected to carry. This is a
# starting vocabulary, not an exhaustive one; add more fields with
# `coquena field define` as they are needed.

set -eu

COQUENA_BIN="${COQUENA_BIN:-coquena}"

define() {
  "$COQUENA_BIN" field define "$@" --overwrite
}

define integer ctx-size --default 0 --provider official
define boolean mmap --default true --provider official
define boolean kv-unified --default true --provider official
define integer parallel --default 4 --provider official
define string temp --default 0.8 --provider official
define integer top-k --default 40 --provider official
define string top-p --default 0.95 --provider official
define string min-p --default 0.05 --provider official
define integer batch-size --default 2048 --provider official
define integer ubatch-size --default 512 --provider official

echo "Seeded llama.cpp field catalog."
