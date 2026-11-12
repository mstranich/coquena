---
status: draft
created: 2026-11-12
tags:
- m5
- perf
created_at: 2026-11-12T03:19:48.433057Z
updated_at: 2026-11-12T03:19:48.433057Z
---

# Comparar mediciones de rendimiento

## Objetivo

Comparar dos o más corridas guardadas (antes y después de un cambio, o variantes
de un preset) y mostrar las diferencias. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- Comparación al estilo de `compare-llama-bench.py` (llama.cpp), sobre los resultados
  guardados por `perf`, en CLI y en la pestaña Rendimiento.
- Señalar cuando las corridas no son comparables (otro backend, hardware o carga).

## Dependencias

- [096](../096-m5-perf-medir/README.md): `perf` y los resultados guardados que esta tarjeta usa.

## Por concretar

- [ ] Qué magnitudes se comparan y con qué umbral se marca una diferencia.

## Criterios de aceptación (borrador)

- [ ] Comparación de corridas reales con diferencias señaladas.
- [ ] Publicación Native AOT y ejecución del binario.
