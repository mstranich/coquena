---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
- 031-m2-model-upgrade
created_at: 2026-10-10T16:36:45.019442500Z
updated_at: 2026-10-10T16:48:26.968458800Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.968458800Z
---
# Convertir un registro externo en instalación gestionada

## Objetivo
Cambiar explícitamente la propiedad de un registro externo tras verificar una nueva descarga
gestionada.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#pasar-un-externo-a-gestión-de-coquena)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Ofrecer transición explícita al descargar nombre ocupado por externo; cancelar/rechazar
  conserva el registro, otras colisiones se rechazan.
- [ ] Completar/verificar descarga antes de cambiar propiedad/rutas conservando
  ID/referencias; nunca tocar archivos externos originales.
- [ ] Comprobar vigencia antes de publicar y probar principal/companion/shards, fallos e
  interrupciones sin retirar prematuramente el externo.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No mueve, borra ni adopta los archivos externos originales.
