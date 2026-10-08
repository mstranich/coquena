---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '010'
created_at: 2026-10-07T22:02:20.212362700Z
updated_at: 2026-10-07T22:02:20.212362700Z
---

# Definiciones de servidor, defaults y puertos

## Objetivo
Integrar create/default/use/set/unset/preset/show/list y resolución de nombre opcional con palabras reservadas. Persistir intención de backend y presets asociados; host/port/metrics estáticos, métricas true. Componer plan de inicio con catálogos inyectados, sin lanzar procesos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [ ] Probar server create default y server default default, herencia backend, latest/pin, selección ausente, preset inválido y puerto único. Asignación automática max+1/8080 inicial informa valor y rechaza overflow sin envolver. Inicio sin sustitutos ni presets omitidos.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
