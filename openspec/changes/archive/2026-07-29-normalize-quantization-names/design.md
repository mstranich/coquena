## Context

El change `classify-models-and-companions` ya implementó la gramática `<user>/<repo>:<quant>` internamente:
- `ModelReference{Raw, Repository, Quant}` en `internal/presets/types.go`
- `parseRef()` en `internal/model/huggingface.go` divide la referencia en `repo` y `quant`
- `resolveLibrary(reference, state)` en `internal/model/offline.go` usa esta gramática
- Sidecar JSON tiene `"fileType": 15` (entero), sin nombre legible de cuantización

**Problema**: La tabla visible de `coquena model list` en `cmd/coquena/model.go` muestra REPOSITORY y FILENAME como columnas separadas, pero el usuario quiere ver `<user>/<repo>:<quant>` como una identidad unificada.

**Fuente de la tabla fileType→nombre**: `ggml/include/ggml.h` de llama.cpp contiene `enum ggml_type` con 43 valores activos. Los valores eliminados (Q4_2, Q4_3, Q4_0_4_4, Q4_0_4_8, Q4_0_8_8, IQ4_NL_4_4, IQ4_NL_4_8, IQ4_NL_8_8) están comentados como "support has been removed".

## Goals / Non-Goals

**Goals:**
- Mostrar `<user>/<repo>:<quant>` como columna REFERENCE en la tabla visible de `coquena model list`.
- Agregar constante Go `fileTypeNames` con el mapeo fileType→nombre legible.
- Mantener la columna FILENAME para visualizar la variante específica (con o sin imatrix, con o sin calibración, etc.).
- Incluir tipos eliminados con nombres descriptivos como "removed".

**Non-Goals:**
- Regenerar sidecars existentes (el campo `fileType` es suficiente).
- Soportar nuevos tipos de cuantización (fuera de alcance de este change).
- Modificar la gramática de referencia (ya implementada en `parseRef()`).

## Decisions

### 1. Fuente de los nombres de cuantización

**Decisión**: Usar `enum ggml_type` de `ggml.h` de llama.cpp como fuente de verdad.

**Alternativas consideradas**:
- **Inferir del nombre del archivo**: Rechazado. El caso `-imatrix` demuestra que el nombre del archivo no es confiable.
- **Agregar nombre al sidecar JSON**: Rechazado. Requiere regenerar todos los sidecars existentes.
- **Usar `GGUFType` de `constants.py`**: No tiene la tabla de fileType→nombre.

**Rationale**: La cuantización es una constante, no derivable del archivo. `ggml.h` es la fuente oficial de llama.cpp.

### 2. Estructura de la constante Go

**Decisión**: Usar `map[int]string` con valores descriptivos para los tipos eliminados.

**Alternativas consideradas**:
- **Solo tipos activos**: Incompleto, los operadores necesitan saber que un tipo está eliminado.
- **Usar `nil` para tipos desconocidos**: Visualmente raro (`: `).
- **Usar `"unknown"` para tipos desconocidos**: Inventaría información.

**Rationale**: Los tipos eliminados son conocidos (documentados en `ggml.h` como "support has been removed"). Incluirlos con nombres descriptivos como "removed" es más informativo que omitirlos.

### 3. Ubicación de la constante

**Decisión**: `internal/model/filetypes.go`.

**Alternativas consideradas**:
- **`internal/model/capability.go`**: Ya tiene constantes de capacidad, pero `filetypes.go` es más específico.
- **`internal/model/types.go`**: Ya tiene tipos de datos, pero `filetypes.go` es más específico.

**Rationale**: `filetypes.go` es un nombre descriptivo y específico para este propósito.

### 4. Formato de la columna REFERENCE

**Decisión**: `<user>/<repo>:<quant>` sin extensión del archivo.

**Alternativas consideradas**:
- **`<user>/<repo>:<quant>.gguf`**: Más informativo, pero la extensión es redundante.
- **`<user>/<repo>:<quant> (fileType=N)`**: Demasiado técnico para la tabla visible.

**Rationale**: La extensión `.gguf` es obvia y redundante. El formato `<user>/<repo>:<quant>` es consistente con la gramática ya implementada en `ModelReference`.

## Risks / Trade-offs

### 1. Tipos desconocidos

**Risk**: Si llama.cpp agrega un nuevo tipo de cuantización sin actualizar `ggml.h` con un nombre, el mapeo no lo incluirá.

**Mitigation**: Los operadores verán `:removed` o `:unknown` en la columna REFERENCE. Se puede agregar un campo opcional `fileTypeName` al sidecar JSON en el futuro.

### 2. Compatibilidad con sidecars existentes

**Risk**: Los sidecars existentes tienen `fileType` como entero, sin nombre legible.

**Mitigation**: No se requiere regenerar sidecars. El campo `fileType` es suficiente para el mapeo.

### 3. Nombres de cuantización en español

**Risk**: Los nombres de cuantización en `ggml.h` están en inglés (ej. `Q4_K_M`).

**Mitigation**: Los nombres de cuantización son estándares de la industria (llama.cpp, HuggingFace, etc.) y están en inglés. No se traducen.

## Migration Plan

Ninguno. El cambio es puramente de visualización y no requiere migración de datos.

## Open Questions

Ninguno. La exploración previa confirmó que la fuente de verdad es `ggml.h` de llama.cpp y que no se requiere regenerar sidecars.
