## Context

`internal/model/filetypes.go` contiene un único `map[int64]string` (`FileTypeNames`) que mezcla tipos activos con tipos deprecados. Los 8 tipos deprecados (fileType 4, 5, 31, 32, 33, 36, 37, 38) llevan el sufijo " (removed)" en sus nombres. Este sufijo aparece en la columna QUANT de `coquena model list` y en `FormatReference`, transmitiendo la señal incorrecta de que el tipo no funciona.

Todos los tipos deprecados están en el rango "ordinary" (fileType < 40), y son cuantizaciones que aún funcionan — especialmente para companion models (projectors) cuyos archivos pequeños usan estos formatos.

## Goals / Non-Goals

**Goals:**
- Eliminar el texto "(removed)" de la columna QUANT en `model list` y de `FormatReference`
- Separar conceptualmente tipos activos de tipos deprecados en el código
- Mantener los tipos deprecados como discoverables (no `unknown`)

**Non-Goals:**
- No cambiar qué tipos considera llama.cpp como soportados
- No añadir nueva funcionalidad de display o metadata
- No modificar la estructura de sidecar ni el schema de clasificación

## Decisions

### Decision 1: Dos maps en el mismo archivo

**Elección**: `FileTypeNames` (activos) + `DeprecatedFileTypeNames` (deprecados) en `internal/model/filetypes.go`.

**Rationale**: El archivo ya es la fuente de verdad del mapeo fileType→nombre. Separar en dos maps dentro del mismo archivo mantiene la localidad sin fragmentar innecesariamente. Un archivo separado para 8 entradas sería over-engineering.

**Alternativa considerada**: Un solo map con una función de post-procesamiento que remueve el sufijo. Descartada porque no separa responsabilidades — el map sigue mezclando dos conceptos distintos.

### Decision 2: Función de lookup package-level

**Elección**: `fileTypeDisplayName(ft int64) string` en el paquete `model`, consulta `FileTypeNames` primero, luego `DeprecatedFileTypeNames`, retorna "unknown" si no encuentra nada.

**Rationale**: Unifica el punto de consulta. Los consumers (`computeQuant`, `FormatReference`) no necesitan saber qué map contiene el tipo. Si en el futuro se quiere añadir metadata a los deprecados (ej. release donde se removió), solo cambia la función de lookup.

**Alternativas consideradas**:
- Inline checking en cada consumer: duplica lógica, inconsistencia futura.
- Pre-processar el map al inicio: añade complejidad innecesaria para 8 entradas.

### Decision 3: Nombres limpios sin sufijo

**Elección**: `DeprecatedFileTypeNames` almacena `"Q4_2"`, no `"Q4_2 (removed)"`.

**Rationale**: El objetivo es que el display no muestre "(removed)". Si el map ya tiene el nombre limpio, el lookup devuelve directamente el valor correcto sin post-procesamiento.

**Alternativa considerada**: Mantener "(removed)" en el map y stripping en el lookup. Descartada porque el sufijo no tiene lugar en ningún contexto — ni display, ni referencia, ni debugging.

## Risks / Trade-offs

[Risk] Consumidores externos que parsean `FormatReference` esperan "(removed)" en el quant.
→ Mitigation: `FormatReference` es interna a Coquena (usada por CLI y API interna). No es un contrato público con clientes externos.

[Risk] Perder la señal de que un tipo está deprecado completamente.
→ Mitigation: Los tipos deprecados siguen siendo legibles vía `DeprecatedFileTypeNames`. Un operador curioso puede consultar el código o la documentación. La señal de deprecación es información para el desarrollador, no para el operador en la tabla.

[Trade-off] Dos maps vs uno solo: más código de lookup, pero separación clara de responsabilidades.
→ El costo es una función adicional, pero el beneficio es que el intent del código es claro: `FileTypeNames` = activo, `DeprecatedFileTypeNames` = deprecado.
