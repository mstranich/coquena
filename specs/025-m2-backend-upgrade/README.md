---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 024-m2-backend-update
created_at: 2026-10-10T16:36:43.026561600Z
updated_at: 2026-10-10T16:48:26.391011700Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.391011700Z
---
# Actualizar backends y aplicar retención de builds

## Objetivo
Actualizar seguimientos latest con el build detectado y aplicar la política de retención de
instalaciones anteriores.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/servidores.md#definición-persistida)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Aplicar uno o todos los planes latest usando el build detectado por update sin resolver
  otro silenciosamente.
- [ ] Reutilizar transferencia, validación y publicación del instalador; conservar instalación
  anterior ante fallos y probar pausa/reanudación/cancelación por unidad.
- [ ] Actualizar build actual para futuros inicios sin cambiar instancias activas o pins ni
  actualizar externos.
- [ ] Probar retención por origen/familia/plataforma, límite inicial y protección de
  actual/fijados/en uso, con diagnóstico si los protegidos exceden el límite.
- [ ] Verificar config backend-retention show/set/unset, persistencia del límite y
  origen informado; rechazar valores inválidos sin cambios. Consultar/cambiar el límite
  no elimina builds: la limpieza se aplica tras un upgrade exitoso.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No actualiza externos ni amplía familias oficiales; la limpieza conserva instalaciones
protegidas.
