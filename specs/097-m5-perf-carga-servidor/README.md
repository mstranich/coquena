---
status: draft
created: 2026-11-12
tags:
- m5
- perf
created_at: 2026-11-12T03:19:48.198743300Z
updated_at: 2026-11-12T03:19:48.198743300Z
---

# Medir carga del servidor con perf

## Objetivo

Medir el servicio publicado bajo carga: peticiones secuenciales y concurrentes
contra `llama-server`, con tasas, latencia y consumo. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- Llevar a C# las pruebas de `server-bench.py` (llama.cpp) y las de concurrencia,
  round-robin e hilos de llama-throughput-lab, sin su menú, nginx ni scripts de shell.
- Usar entradas publicadas por Coquena según [benchmarking](../../docs/design/benchmarking.md).

## Por concretar

- [ ] Qué pruebas entran primero y sus parámetros por defecto.
- [ ] Cómo se evita afectar un servicio en uso.

## Criterios de aceptación (borrador)

- [ ] Resultados comparables con la medición de rendimiento con perf.
- [ ] Publicación Native AOT y ejecución del binario.
