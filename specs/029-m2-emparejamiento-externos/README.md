---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
created_at: 2026-10-10T16:36:44.181081300Z
updated_at: 2026-10-10T16:48:26.722191300Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.722191300Z
---
# Emparejar externos mediante selección asistida

## Objetivo
Asistir el emparejamiento remoto opcional conservando siempre el registro local ya creado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#incorporación-externo-o-gestionado)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Completar --try-match en altas de principal/companion; sin flag no consultar red y
  persistir el alta antes del menú.
- [ ] Mostrar candidatos/razones y selección o cancelación explícita; guardar equivalencia
  informativa sin cambiar ID/nombre/propiedad ni habilitar upgrade.
- [ ] Probar ambigüedad, ausencia de candidatos, fallo remoto y falta de interacción
  conservando alta local; consultas acotadas sin descargar pesos.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No certifica procedencia o identidad de bytes ni convierte externos en gestionados.
