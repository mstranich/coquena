# Recipes

A recipe is a YAML file that defines a service, consumer presets, or both. You
can write one by hand or receive one from someone else and run it directly.
`coquena init` is an optional shortcut that creates a minimal recipe.

## Top-level keys

| Key | Purpose |
| --- | --- |
| `name` | Recipe name. Published presets become `name/preset`. If omitted, the folder name is used the first time the recipe is registered. |
| `target` | Where presets are published: `global` (default) or `self`. |
| `args-mode` | `permissive` (default) or `strict` argument checking. |
| `service` | The service this recipe defines. A recipe has at most one. |
| `arg-defs` | Custom argument definitions. |
| `models` | Named model selections. |
| `companions` | Named auxiliary files, such as `mmproj` projectors. |
| `presets` | Named configurations that reference models. |

Unknown structural keys are rejected. YAML anchors, aliases, and merge keys are
not supported.

## Choosing a target

- `target: global` publishes to the default global service. This is the default.
- `target: self` publishes to the local service defined in the same recipe, which
  must include a `service` block.

See [Global and local](global-and-local.md).

## Presets

The `presets` block is the heart of most recipes. See
[Models and Presets](models-and-presets.md) for what presets are and how to configure
them from a recipe or the CLI.

## Write argument values

Argument names match `llama-server` options. The YAML type decides what is sent:

| In the recipe | Sent to `llama-server` |
| --- | --- |
| `ctx-size: 8192` | `--ctx-size 8192` |
| `metrics: true` | `--metrics` |
| `metrics: false` | Nothing |
| `some-option: "true"` | `--some-option true` |

Arguments you leave out keep the executable's own defaults.

In YAML, only `true` and `false` are booleans. Values such as `on` and `yes` are sent
as text, so `flash-attn: on` passes `on` to the server.

## Define your own arguments

You decide how strict a recipe is, much as TypeScript lets you mix typed and untyped
code. Coquena does not need to know an argument for you to use it, so a new llama.cpp
release or a fork's option works right away.

| Level | What happens |
| --- | --- |
| No definition | In the default `permissive` mode, the argument is passed as written. |
| With a definition | Coquena checks its type and limits, in either mode. |
| `args-mode: strict` | Every argument needs a definition. Set it at the top of a recipe, or in a single preset. |

Definitions go under `arg-defs`. Keeping them in their own layer lets you add or remove
the checks with `-f`:

```yaml
arg-defs:
  definitions:
    my-fork-option:
      type: integer
      minimum: 1
      maximum: 8
      description: Number of experimental workers
```

`type` is `string`, `integer`, `number`, `boolean`, or `array`. Definitions can also
use `enum`, `pattern`, `aliases`, and `rules` between arguments. Setting a definition
to `null` removes an inherited one.

### Limit an argument to some builds

`applies-to` declares which official builds accept an argument, inclusive on both ends.
Use it when a release adds or deprecates an option:

```yaml
arg-defs:
  definitions:
    old-option:
      type: integer
      applies-to:
        max-build: b11000
```

`coquena up` stops with `argument-not-applicable` if the service's build is outside the
range. To check a recipe against a release, add a layer with these definitions and run
`up`. Build ranges apply only to official builds, because a fork has no comparable
version number. `coquena verify` does not check them yet.

To keep definitions outside a recipe, use `coquena param define <name> --file
<definition.yaml>`.

## Keep several profiles

A recipe can hold the alternatives you are comparing, so they can be versioned
and shared with the project. These values are illustrations: tune them for your
model and memory. GPU offload needs a suitable CUDA or Vulkan backend.

```yaml
name: my-lab
target: global
models:
  hf-model: Qwen/Qwen2.5-1.5B-Instruct-GGUF:Q4_K_M
  local-model:
    files: [./weights/local-model.gguf]
presets:
  short-context:
    args:
      model: model://hf-model
      ctx-size: 8192
      n-gpu-layers: 99
      threads: 4
      cache-type-k: q8_0
      cache-type-v: q8_0
      flash-attn: on
  long-context:
    args:
      model: model://hf-model
      ctx-size: 32768
      n-gpu-layers: 20
      threads: 8
      cache-type-k: q4_0
      cache-type-v: q4_0
      flash-attn: on
  local-experiment:
    enable: false
    args:
      model: model://local-model
      ctx-size: 16384
      n-gpu-layers: 0
      threads: 8
```

Publish with `coquena up --start-service`, then select `my-lab/short-context` or
`my-lab/long-context` in your request. To add `my-lab/local-experiment`, provide
the local file, set `enable` to `true`, and run `coquena up` again.

## Iterate on a live router

Finding a good balance between your hardware and model speed takes trial and error.
Change a preset or switch its model, then run `coquena up` again. The router keeps
running: new presets are added, changed ones are reloaded, and unchanged ones stay
as they are.

```powershell
coquena preset set chat ctx-size 16384
coquena up
```

A change can unload a model and interrupt its requests, so Coquena asks for
confirmation when it observes activity.

## Combine layers

Pass `-f` several times to combine YAML files in order. Later layers override
earlier ones, and the commands that edit a recipe write to the last layer.

```powershell
coquena up -f coquena.yaml -f coquena.gpu.yaml
```

Each layer that declares `service.backend` replaces that block completely. Other
`service` keys are merged key by key. Running `up` with a different combination
reconciles the project's presets: it adds new ones, updates changed ones, and
withdraws those that are no longer enabled. Other projects' presets are kept.

A later layer only needs what it changes. Set a key to `null` to remove it. This
layer removes one argument from `chat` and drops the `other` preset:

```yaml
presets:
  chat:
    args:
      flash-attn: null
  other: null
```

Paths in every layer are relative to the project folder. Use `--project-directory`
(alias `--base`) to set it.

## Use variables

Recipes can read environment variables, so one recipe can serve several machines:

```yaml
presets:
  chat:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
      ctx-size: ${CTX:-4096|integer}
```

| Syntax | Meaning |
| --- | --- |
| `${NAME}` | Required. Coquena stops with an error if it is not set. |
| `${NAME:-value}` | Uses `value` if `NAME` is not set. |
| `${NAME\|integer}` | Converts the text to an integer. Numeric arguments need this. |

Coquena does not load `.env` files on its own. Pass them explicitly:

```powershell
coquena up --env-file lab.env
```

A `.env` file holds `NAME=value` lines. Variables already set in your environment
take priority over the file. `coquena preset show` displays these values as
`<redacted>`, so secrets are not printed.

A required variable must be set even if a later layer removes the preset that uses
it, because each layer is checked before they are combined.

## Share a recipe

Commit `coquena.yaml` and any layers to Git. Coquena's `.gitignore` files keep
models, backends, logs, and local state out of the repository. `coquena init` also
ignores `.env`; do the same if you did not use it.

Someone who receives the recipe runs `coquena up`. Hugging Face models and official
backends download automatically, but files declared with a local path must be
supplied at the same relative location.

## Edit from the CLI

Commands such as `preset set` and `model set` update the recipe and keep comments and
unrelated text. See [Models and Presets](models-and-presets.md).

With a multi-layer recipe, these commands write to the **last** layer you select. This
changes `coquena.gpu.yaml` and leaves `coquena.yaml` untouched:

```powershell
coquena preset set chat ctx-size 2048 -f coquena.yaml -f coquena.gpu.yaml
```

Saving a recipe does not change a running service. Run `coquena up` to apply it.

## Check a recipe

`coquena verify` checks a recipe and its resources without downloading anything:

```powershell
coquena verify
```

If the recipe is invalid, the error names the file, line, and column, then the key and
the problem. Fix it and run the command again, because errors are reported one at a
time.

```text
Error en la receta: coquena.yaml:3:10 $["unknown"] [structure/unknown-key] Clave estructural desconocida.
```

| Code | Meaning |
| --- | --- |
| `yaml/yaml-syntax` | The file is not valid YAML. |
| `structure/unknown-key` | A key is misspelled or not supported in that place. |
| `validation/unknown-resource-reference` | A `model://` or `companion://` reference has no matching entry. |
| `validation/unknown-argument` | An argument has no definition in `strict` mode. |
| `interpolation/variable-required` | A required variable is not set. |

A valid recipe can still report resources to review, such as a model that has not been
downloaded yet. `coquena up` prepares those.
