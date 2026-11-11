---
status: draft
created: 2026-11-11
priority: medium
tags:
- m4
created_at: 2026-11-11T13:06:39.637477700Z
updated_at: 2026-11-11T13:07:22.954120900Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.726586700Z
---
# Reproducir la coordinación entre monitor de descargas y limpieza

## Objetivo

Comprobar sobre el código actual si persiste la carrera entre el monitor de
descargas y la limpieza, antes de decidir una corrección.
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

La carrera se reprodujo en código anterior a 054
([hallazgo 002](../../docs/findings/002-coordinacion-monitor-limpieza.md)); no se sabe
si los cambios posteriores la resolvieron.

## Alcance propuesto

- Reproducción con un caso automatizado sobre ámbitos aislados.
- Si no se reproduce, cerrar el hallazgo con esa evidencia.
- Si se reproduce, documentar causa y propuesta para una tarea de corrección.

## Por concretar

- [ ] Escenario y tiempos que dispararon la carrera original.

## Criterios de aceptación (borrador)

- [ ] Evidencia reproducible de presencia o ausencia de la carrera.
- [ ] Sin corrección dentro de esta tarea.
