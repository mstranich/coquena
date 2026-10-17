---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.525824600Z
updated_at: 2026-10-21T23:16:24.525824600Z
---

# Descargar una cuantización explícita inequívoca sin interacción redundante

## Objetivo

Corregir el camino actual de descarga cuando el selector identifica una única unidad válida.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md#model](../../docs/comandos.md#model)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [descargas.md](../../docs/descargas.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [ ] Reproducir con fixtures la interacción redundante para un selector como unsloth/gemma-4-12b-it-GGUF:Q4_0, sin descargar pesos costosos.
- [ ] Descargar directamente una cuantización explícita con una unidad válida, sin menú, filtrado ni oferta de README.
- [ ] Contar unidades, no archivos: un conjunto de shards coherente puede ser la única unidad válida.
- [ ] Conservar selección ante varias unidades y diagnóstico ante ausencia o resultado no concluyente; nunca sustituir cuantización ni añadir companions implícitos.
- [ ] Mantener integridad, validación GGUF, reanudación y el recorrido sin cuantización explícita.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Correctivo independiente sobre la base M2; no depende del compositor ni implementa recursos declarativos de proyecto.

## Estado y evidencia

Planificada. Sin implementación ni validación ejecutadas. El inicio del trabajo, el cierre y cualquier commit requieren la autorización correspondiente.
