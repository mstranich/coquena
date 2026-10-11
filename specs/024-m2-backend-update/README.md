---
status: planned
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:42.739369Z
updated_at: 2026-10-10T16:48:26.310946200Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.310946200Z
---
# Consultar actualizaciones de backends oficiales

## Objetivo
Separar la consulta de novedades de la instalación, conservando un candidato por seguimiento
latest.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/servidores.md#definición-persistida)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Consultar todos los seguimientos latest o uno seleccionado y guardar
  candidato/filtro/diagnóstico sin descargar ni cambiar el build instalado.
- [ ] Excluir pins y externos; un error no impide consultar los demás seguimientos.
- [ ] Probar novedades, ausencia de cambios, red fallida y persistencia con fixtures
  oficiales; reutilizar fuente y resolución de 005/006.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No instala builds ni modifica instancias activas. Reutiliza la fuente oficial de 005/006.
