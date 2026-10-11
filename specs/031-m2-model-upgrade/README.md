---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 030-m2-model-update
created_at: 2026-10-10T16:36:44.729429400Z
updated_at: 2026-10-10T16:48:26.888750100Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.888750100Z
---
# Aplicar actualizaciones y reubicaciones de unidades gestionadas

## Objetivo
Publicar la revisión planificada y sus renombres conservando identidad, referencias y
recuperación ante fallos.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#seguimiento-y-actualización-de-gestionados)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Aplicar planes resueltos por update con vigencia/commit comprobados; reutilizar
  contenido local verificado y publicar sólo conjuntos completos.
- [ ] Probar actualización de shards sin mezcla de revisiones y renombre físico conservando
  ID/referencias sin sobrescribir archivos ajenos.
- [ ] Bloquear unidades en uso antes de mover/descargar y coordinar con start; probar
  carreras, espacio insuficiente, cancelación y recuperación entre disco/índice.
- [ ] Conservar instalación anterior ante fallos y limpiar sólo archivos propios después de
  publicar; respetar ubicaciones registradas/configuradas.
- [ ] Cubrir alcance global por unidad y companion; no exigir atomicidad global ni implementar
  transferencia diferencial.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No cambia silenciosamente el commit del plan ni incorpora transferencia diferencial.
