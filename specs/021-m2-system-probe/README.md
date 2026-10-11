---
status: planned
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.495445100Z
updated_at: 2026-10-10T12:29:25.357764100Z
transitions:
- status: planned
  at: 2026-10-10T12:29:25.357764100Z
---
# Separar el sondeo del sistema mediante system probe

## Objetivo
Exponer el sondeo de SO y hardware mediante system probe.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#system)

## Trabajo y aceptación
- [ ] Trasladar el sondeo existente a system probe conservando informe y diagnósticos.
- [ ] System sin subcomando no ejecuta el sondeo; muestra uso con el diagnóstico normal
  de argumentos incompletos. No se mantiene la invocación anterior como alias.
- [ ] Rechazar argumentos adicionales no definidos antes de iniciar el sondeo.
- [ ] Actualizar ayuda y pruebas del despacho CLI.
- [ ] Publicar y ejecutar Native AOT win-x64; registrar evidencia.

## Límites
No modifica la detección de hardware ni añade ajustes del manager o nuevas capacidades
de sondeo. No requiere avisos de deprecación ni mecanismos de transición.
