---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '012'
created_at: 2026-10-07T22:02:22.125315100Z
updated_at: 2026-10-07T22:02:22.125315100Z
---

# Instantánea de métricas y consulta de logs

## Objetivo
Integrar server ps con métricas por modelo y server logs/-f para instancia actual o último log. Concretar referencia de último log y retención limitada, sin inventar historial de instancias activas.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [servidores.md](../../docs/servidores.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Fixtures prueban métricas desactivadas, timeout/formato inválido y modelo no cargado sin cifras inventadas. Ctrl+C de logs -f termina seguimiento, no servidor. Probar retención sin borrar log activo y acceso tras stop; no incorporar TUI.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
