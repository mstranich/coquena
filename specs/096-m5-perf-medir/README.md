---
status: draft
created: 2026-11-12
tags:
- m5
- cli
- perf
created_at: 2026-11-12T03:19:47.968769Z
updated_at: 2026-11-12T03:19:47.968769Z
---

# Medir rendimiento con perf

## Objetivo

Crear el subcomando `perf` y su primera medición: rendimiento de prompt y de
generación de un modelo con `llama-bench` del backend instalado, con resultados
guardados y comparables. Entrega de [M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- `perf` con la medición básica según [benchmarking](../../docs/design/benchmarking.md).
- `perf` ofrece en la CLI lo mismo que la pestaña Rendimiento: misma operación y
  mismos resultados, con salida de texto en lugar de la vista.
- Ejecutar `llama-bench` del backend elegido y leer su salida JSON.
- Guardar cada corrida con backend/build, modelo, argumentos y hardware.

## Por concretar

- [ ] Si los backends instalados conservan `llama-bench` y `llama-batched-bench`.
- [ ] Dónde se guardan los resultados y cuánto se conservan.
- [ ] Barridos admitidos en la primera entrega (`-ngl`, hilos, `-fa`, lote).

## Criterios de aceptación (borrador)

- [ ] Medición reproducible con resultado guardado.
- [ ] Publicación Native AOT y ejecución del binario.
