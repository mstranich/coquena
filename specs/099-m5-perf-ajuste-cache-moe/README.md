---
status: draft
created: 2026-11-12
tags:
- m5
- perf
- moe
created_at: 2026-11-12T03:19:48.687952800Z
updated_at: 2026-11-12T03:19:48.687952800Z
---

# Ajustar la caché MoE de un preset

## Objetivo

Proponer `--moe-cache-mib` y `--moe-cache-cpu-share` para un preset midiendo el
servidor real, y escribirlos en la receta con confirmación. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- Llevar a C# `scripts/moe-cache-tune.py` de llama.cpp: corridas de calibración,
  modelo de tiempos y simulación de la caché sobre la traza de expertos.
- Habilitarlo sólo con backends que soportan los parámetros de caché MoE
  ([093](../093-incorporar-cache-moe/README.md)).
- Mostrar la predicción por tamaño y proporción; escribir la recomendación en la
  receta sólo con confirmación y guardar la evidencia.

## Riesgos

- Depende de la traza `LLAMA_MOE_CACHE_TRACE` y de mensajes de log de llama.cpp,
  que son internos y pueden cambiar entre versiones.

## Por concretar

- [ ] Versión mínima del backend que produce traza y logs compatibles.
- [ ] Cómo se detiene el servidor de calibración en Windows sin perder datos.

## Criterios de aceptación (borrador)

- [ ] Recomendación reproducible sobre un modelo MoE real.
- [ ] Publicación Native AOT y ejecución del binario.
