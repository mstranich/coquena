## Context

The embedded Angular application currently has a single shell and no client-side routes. Coquena needs to make llama.cpp configuration explicit while allowing users to define fields for upstream or custom builds. This first slice is deliberately a UI/UX experiment: all catalog and preset data exists only for the lifetime of the browser application.

llama.cpp handler categories inform the field kinds: string, integer, boolean, and void. The catalog also needs enumerative fields for constrained values such as quantization choices. A preset uses a catalog field's identity and supplies a compatible value; it does not repeat the field kind.

## Goals / Non-Goals

**Goals:**

- Add navigable Angular pages for catalog fields and presets with a compact, keyboard-oriented visual language.
- Keep shared catalog and preset state while the user navigates between pages.
- Provide type-appropriate editing and validation for field definitions and preset entries.
- Make dependencies between a catalog field and presets visible and safe during editing.

**Non-Goals:**

- Backend APIs, disk persistence, browser-storage persistence, import/export, and synchronization.
- Inspecting `llama-server --help` or any binary to discover fields.
- Executing a generated command, starting a process, or validating against a llama.cpp installation.
- A complete declarative representation of every llama.cpp option, grouping, documentation, or version/build metadata.
- Adding Angular Material, another full visual component library, NgRx, or another third-party state-management dependency.

## Decisions

### Use an application-level Angular Signals store

Create one injectable `FieldCatalogStore` responsible for field definitions and presets. It exposes read-only signals for template consumption and explicit mutation methods for CRUD operations. Both routed pages consume that store, so state survives ordinary client-side navigation but is lost on refresh.

This is state management, not component-local state. Signals match the existing Angular direction and are enough while there are no remote effects, persistence, or independent feature modules. A future API-backed store can preserve the public store contract while changing its source of truth.

**Alternative considered:** NgRx or another external store library. Rejected for this slice because it adds actions, reducers, effects, and dependency surface before asynchronous workflows justify them.

### Build the compact UI from semantic controls and Angular CDK

Use semantic HTML controls, Angular forms, and custom SCSS for the visual surface. Add Angular CDK only for interaction primitives that native controls do not adequately provide, especially keyboard-managed menus and focus behavior. Use a small local SVG icon set rather than a remote icon font.

The catalog command bar contains a name filter and compact direct controls to add each of the five field kinds. Each preset displays every currently eligible, unused catalog field as a chip-like button, allowing selection at a glance without opening a menu. Existing fields and enum options expose compact delete buttons with accessible labels.

Each type control immediately inserts a field of that immutable kind as an empty draft and focuses its argument-name input. Fields are edited in place as compact cards; there is no separate global save form. Each card owns its delete and save/finish actions. Saving a named draft finalizes it, while saving an existing field commits its edited metadata. Child type controls render below the parent's existing nested children; the child kind is independent of the parent's kind, so the relationship is structural rather than an arbitrary parent selector in a form.

**Alternative considered:** Angular Material plus Material Symbols. Rejected because its complete component system and visual language are unnecessary for this compact, product-specific UI.

### Model a field definition separately from a preset entry

`FieldDefinition` has a stable browser-local id, an argument name, kind, and kind-specific metadata. `PresetEntry` stores the referenced field id and a value only when its kind accepts one. `Preset` owns a name and a collection of entries.

The supported kinds are:

| Kind | Catalog metadata | Preset editor/value |
| --- | --- | --- |
| `string` | optional default string | text input |
| `integer` | optional default integer | integer input |
| `boolean` | optional default boolean and optional false-argument name | boolean control |
| `void` | none | presence control; no value is stored |
| `enum` | non-empty distinct string options and optional default option | select control |

The optional boolean false-argument preserves the UI model needed for handlers that map false to a negated CLI flag, without generating or executing the command in this change.

**Alternative considered:** Store every preset setting as `{ name, type, value }`. Rejected because a type copied into the preset can diverge from its catalog definition and violates the intended catalog-first workflow.

### Derive preset controls from the selected field definition

The preset editor first selects an unused catalog field. The store resolves that id to a definition and the view renders the appropriate control. Defaults seed a newly added entry when present. Void entries render as an enabled/present setting rather than an input value.

The editor forbids duplicate field ids in one preset. It also rejects a value outside the selected enum options and rejects non-integer values for integer fields.

Enum options are edited incrementally in the field editor: an input and add control add one option at a time, and each existing option has its own removal control. This avoids requiring a user to manipulate a serialized comma-separated list.

### Model parent fields as activation prerequisites

A field can optionally declare a dependency on a parent field plus the parent state that activates it: presence for a void field, `true` for a boolean field, or a specific value for string, integer, or enum fields. The catalog visually nests dependent fields under their parent, but the relationship has behavioral meaning.

The preset's available-field chips only include a dependent field after its prerequisite is satisfied in that preset. Removing or changing a parent in a way that invalidates configured children is blocked until those child entries are removed or made valid. This covers the speculative-prediction pattern: a void activation field enables optional dependent model fields.

Mutual exclusion is deliberately not modeled in this change because no confirmed llama.cpp examples have yet been identified.

### Preserve referential integrity in the in-memory store

Removing a catalog field atomically removes its descendant subtree and every corresponding preset entry, which keeps the browser-memory graph consistent while making the Remove action immediate. Field kinds remain immutable after creation.

**Alternative considered:** Blocking removal while references exist. Rejected for this in-memory UX because it makes the visible Remove action appear broken; atomic subtree/reference cleanup is deterministic and leaves no dangling state.

### Use routes and a persistent navigation shell

The root application component gains a small navigation bar with links to `/fields` and `/presets`; routed components render inside the existing application shell. The existing Web UI server's SPA fallback already supports direct client-side route refreshes.

## Risks / Trade-offs

- [All data disappears on refresh or closing the tab] → Make the in-memory scope explicit in page copy and defer persistence intentionally.
- [A custom build needs richer types later] → Keep kinds as a discriminated model and add new kind metadata rather than overloading strings.
- [Catalog edits can affect presets] → Cascade field subtree deletion through corresponding preset entries and keep field kinds immutable.
- [A custom Signals store later needs remote behavior] → Keep mutations behind store methods rather than writing signals directly from components.
- [Boolean CLI semantics vary] → Record the optional false-argument metadata, but defer command serialization to a later runtime feature.
- [Future interactions may need richer keyboard primitives] → Add narrowly scoped Angular CDK primitives only when native controls are insufficient.
- [Unconfirmed mutual exclusions could complicate the data model] → Model only prerequisite dependencies now and defer conflict rules until real examples are available.

### Represent field names as INI keys

Catalog names are bare INI keys without the CLI -- prefix. CLI decoration belongs to a later serializer; keeping it out of the catalog lets the same field identity map cleanly to INI files.

### Place creation actions after content

Type controls and preset creation controls appear after their respective lists. This makes sequential keyboard tabbing follow the content from top to bottom and returns focus to the new draft input after insertion. Per-field update, child, and delete actions remain grouped in the field card footer.

### Keep child authoring local to confirmed field cards

Confirmed fields expose an Allow children toggle. Enabling it renders a nested children region below the parent row: existing child field nodes come first and the five child-kind creation controls come after them. Child nodes use the same recursive layout, so they can independently allow deeper children. Global creation controls remain below all root nodes. Draft nodes omit the toggle until finalized. Disabling is rejected while children exist.
