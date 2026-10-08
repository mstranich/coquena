---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '001'
created_at: 2026-10-07T22:02:11.164831800Z
updated_at: 2026-10-07T22:02:11.164831800Z
---

# Contratos y fixtures mínimos de M1

## Objetivo
Definir datos mínimos de backend, selección remota, unidad GGUF, preset, servidor, operación e instancia. Usar IDs estables y JSON con serialización compatible con AOT; no crear un framework ni implementar M2.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [modelo-de-datos.md](../../docs/modelo-de-datos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [ ] Round-trip de fixtures pequeños; distinguir intención de selección y ejecución concreta. Revisar esquema de servidores acordado y diagnosticar versión desconocida/documento inválido.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
