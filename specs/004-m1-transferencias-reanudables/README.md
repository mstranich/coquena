---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
created_at: 2026-10-07T22:02:13.145733200Z
updated_at: 2026-10-07T22:02:13.145733200Z
---

# Transferencias y comandos de descarga reanudable

## Objetivo
Implementar motor común con transporte inyectable, GUID persistido antes de bytes, preparación y progreso por archivo. Integrar download list/status/pause/resume/cancel, foreground y Ctrl+C; selección fijada recibida del consumidor, sin decidir modelo ni hardware.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Fixtures HTTP prueban Range/200/validadores, total desconocido, caída, reintentos acotados, pausa coherente, ejecutor exclusivo y reanudación sin repetir archivos completos. Cancelar limpia sólo preparación; fallo preserva GUID y devuelve prompt. Consumidores posteriores completan publicación/limpieza.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
