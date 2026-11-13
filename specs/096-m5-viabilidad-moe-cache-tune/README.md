---
status: draft
created: 2026-11-12
priority: high
tags:
- m5
- cli
- tool
- moe
created_at: 2026-11-12T03:19:47.968769Z
updated_at: 2026-11-13T17:02:31.034262600Z
---
# Viabilidad de portar moe-cache-tune a C#

## Objetivo y prioridad

Resolver primero la principal incertidumbre de [M5](../../docs/milestones/M5-rendimiento.md):
obtener en C# una recomendación fiable basada en el procedimiento de
`scripts/moe-cache-tune.py`, sin dependencia Python de ejecución. El estudio
[conceptual](../../docs/milestones/M5-estudio-rendimiento.md) es punto de partida,
no evidencia de viabilidad. Esta tarjeta sustituye el anterior borrador de Bench.

## Alcance acotado

- Fijar revisión de referencia y un backend compatible conocido.
- Portar lectura de trazas, simulación de caché, modelo de tiempos y calibración.
- Usar un modelo MoE real y configuración explícita, sin requerir formulario,
  índice de arquitectura, edición de recetas ni banco de pruebas.
- Comprobar cómo iniciar y finalizar procesos de calibración en Windows para
  obtener logs/trazas; conservar cancelación y errores reales.
- Comparar cálculos con evidencia de la referencia fijada. Python puede servir
  como oráculo de desarrollo, nunca como dependencia del producto.
- Conservar valores y criterios de la referencia como base de portabilidad;
  cualquier cambio de política debe explicitarse y acordarse, no improvisarse.

## Decisión de viabilidad

- [ ] Se reproduce lectura/simulación/ajuste con datos de referencia y tolerancias justificadas.
- [ ] Se completa una calibración real con recomendación, error de ajuste y predicciones identificadas.
- [ ] Se publica Native AOT para Windows y se ejecuta el binario con el backend acordado.
- [ ] Se comprueba finalización/cancelación y qué evidencia puede faltar si se fuerza el cierre.
- [ ] Se informa coste observado, restricciones y conclusión fundada: viable,
  viable con restricciones o impedimento concreto. No declarar éxito por compilar.

## Límites

No implementa Bench, throughput, integración TUI, gestión de bancos ni reproducción
del arnés del usuario. No exige una prueba adicional que certifique mejora en todo
trabajo de agente. La recomendación conserva el alcance de la carga de calibración.

## Evidencia pendiente

No se inició implementación ni se ejecutó calibración. Las ayudas de ejecutables
inspeccionadas anteriormente no resuelven esta viabilidad.
