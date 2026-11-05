# Models and Presets

A **model** is a GGUF file or set of files. A **preset** is a named way of running a
model: which model to load and with which `llama-server` arguments. The same model
can have several presets, for example one with a short context and one with a long
context.

Clients see presets as models. A preset `chat` in a recipe named `ai-lab` is published
as `ai-lab/chat`, and a client selects that name in its request. llama.cpp loads the
model when a request needs it.

## Define a preset in a recipe

Recipes are the easiest way to work with presets, because they keep everything in one
file you can version and share. The quickest way to use a model is to name it in the
preset:

```yaml
name: ai-lab
presets:
  chat:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
      ctx-size: 8192
```

Each preset accepts these keys:

| Key | Purpose |
| --- | --- |
| `args` | `llama-server` arguments, written without the leading dashes. See [argument values](recipes.md#write-argument-values). |
| `enable` | `false` keeps the definition without preparing or publishing it. The default is `true`. |
| `warmup` | `true` asks the service to load the model after publishing, without waiting for a request. The default is `false`. |
| `args-mode` | `permissive` or `strict` argument checking for this preset. |

`warmup` does not guarantee the model stays in memory, and it is not llama.cpp's
`--warmup` argument.

## Configure a preset from the CLI

These commands edit the recipe for you, so you can skip the text editor. `preset set`
creates the preset if it does not exist. In an empty folder, this builds the recipe
above, except for its `name`:

```powershell
coquena init
coquena preset set chat model Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
coquena preset set chat ctx-size 8192
```

Inspect what the recipe defines:

```powershell
coquena preset list
coquena preset show chat
```

Copy a preset to try a variation, and remove what you no longer need:

```powershell
coquena preset clone chat chat-long
coquena preset set chat-long ctx-size 32768
coquena preset unset chat-long ctx-size
coquena preset remove chat-long
```

`preset bind` links an argument to a model or companion declared in the recipe:

```powershell
coquena preset bind chat model model://main
coquena preset bind chat mmproj companion://vision
```

## Publish your changes

Editing a preset only changes the recipe. A running service is not affected until you
publish:

```powershell
coquena up
```

`up` publishes all enabled presets. To work with a single preset, use:

```powershell
coquena apply chat
coquena withdraw chat
```

See [Iterate on a live router](recipes.md#iterate-on-a-live-router).

## How to reference a model

A Hugging Face reference has up to three parts:

```text
ggml-org/gemma-3-4b-it-GGUF#mmproj:F16
└──────┘ └────────────────┘ └────┘ └─┘
publisher   repository       type  quantization
```

| Part | Meaning |
| --- | --- |
| `ggml-org` | The Hugging Face user or organization that publishes the files. |
| `gemma-3-4b-it-GGUF` | The repository that holds the GGUF files. |
| `#mmproj` | The [companion](#companions) type. Main models leave it out. |
| `:F16` | The quantization: the file variant to use. Lower-bit variants, such as `Q4_K_M`, use less memory; higher ones, such as `Q8_0` or `F16`, stay closer to the original. |

The repository's Files page on Hugging Face shows which quantizations exist. So:

- `ggml-org/gemma-3-4b-it-GGUF:Q4_K_M` is the main model in its `Q4_K_M` variant.
- `ggml-org/gemma-3-4b-it-GGUF#mmproj:F16` is that repository's `mmproj` companion.

`model://` and `companion://` are different: they point to an entry in the same recipe
by its key, like a local name. Declare the resource once, then use the short name:

| Reference | Points to |
| --- | --- |
| `model://main` | The entry `main` under `models` |
| `companion://vision` | The entry `vision` under `companions` |

If the entry does not exist, `coquena verify` reports
`validation/unknown-resource-reference`.

## Three ways to write a model

These three recipes select the same model. Choose the one that fits.

Directly in the preset, the shortest form:

```yaml
presets:
  chat:
    args:
      model: ggml-org/gemma-3-4b-it-GGUF:Q4_K_M
      ctx-size: 8192
```

Under `models`, in short form. The preset references it as `model://main`:

```yaml
models:
  main: ggml-org/gemma-3-4b-it-GGUF:Q4_K_M
presets:
  chat:
    args:
      model: model://main
      ctx-size: 8192
```

Under `models`, in long form:

```yaml
models:
  main:
    repository: ggml-org/gemma-3-4b-it-GGUF
    quantization: Q4_K_M
presets:
  chat:
    args:
      model: model://main
      ctx-size: 8192
```

Use the `models` block when you want to:

- **Reuse a model in several presets.** Change it in one place and every preset follows.
- **Select it in commands**, for example `coquena verify --select model://main`.

Use the long form to set options the short form cannot express: a `revision`, or
explicit `files` from the repository.

```yaml
models:
  main:
    repository: ggml-org/gemma-3-4b-it-GGUF
    revision: main
    quantization: Q4_K_M
presets:
  short:
    args:
      model: model://main
      ctx-size: 4096
  long:
    args:
      model: model://main
      ctx-size: 32768
```

Coquena downloads only the files the selection needs, including all shards, and
checks them before installing. `up` prepares what enabled presets require.

## Local files

Declare files you supply with a path relative to the project folder:

```yaml
models:
  local:
    files: [./weights/model.gguf]
```

Coquena uses these files where they are and does not copy or adopt them. Someone
who receives the recipe must provide the same relative path. A selection can use
local or remote files, but not both.

## Companions

A companion is an auxiliary file that a preset uses together with its main model. It
is downloaded, verified, and cleaned up like a model, but it does not run by itself.
There are two types:

| Type | Use |
| --- | --- |
| `mmproj` | A multimodal projector, which adds image or audio input to a model that supports it. |
| `mtp` | An auxiliary file for multi-token prediction. |

Declare companions under `companions`, with a Hugging Face selection or a local file,
and pass them to a preset with `companion://key`:

```yaml
models:
  main: ggml-org/gemma-3-4b-it-GGUF:Q4_K_M
companions:
  vision:
    repository: ggml-org/gemma-3-4b-it-GGUF
    type: mmproj
    quantization: F16
presets:
  multimodal:
    args:
      model: model://main
      mmproj: companion://vision
```

From the CLI, `model set` and `model companion set` add these entries to the recipe:

```powershell
coquena model set main --repository ggml-org/gemma-3-4b-it-GGUF --quantization Q4_K_M
coquena model companion set vision --repository ggml-org/gemma-3-4b-it-GGUF --type mmproj --quantization F16
coquena preset bind chat mmproj companion://vision
```

The companion must match the model. Coquena does not check that from the file's name
or type, so use one published for your model.

`model companion` offers the same commands as `model`: `list`, `show`, `add`,
`download`, `remove`, `protect`, and `unprotect`. To download one explicitly, add its
type to the repository:

```powershell
coquena model companion download 'ggml-org/gemma-3-4b-it-GGUF#mmproj:F16'
```

## Manage models from the CLI

```powershell
coquena model list
coquena model show
coquena model download Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
coquena model add .\weights\model.gguf --name my-model
coquena model remove my-model
```

`model show` reads the recipe without downloading. An explicit download protects
the model from cleanup without editing the YAML. `model remove` deletes only files
Coquena owns and keeps external files. `model protect` and `model unprotect` take an
installation ID.

Add `--global` to use the global scope, and `--json` for structured output.

To follow downloads and free disk space, see
[Global and local](global-and-local.md#downloads).
