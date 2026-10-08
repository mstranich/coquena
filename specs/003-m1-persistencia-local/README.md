---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:12.133402100Z
updated_at: 2026-10-07T22:02:12.133402100Z
---

# Home y persistencia local segura

## Objetivo
Resolver home y estructura acordada; guardar catálogos JSON indentados con reemplazo seguro y exclusión breve de escritores. Configuración separada, JSONL de transacciones con límites y sin secretos. Proveer almacenamiento de operaciones e instancias sin implementar sus ciclos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [ ] Probar precedencia de home, dos escritores, JSON dañado y fallo antes/después del reemplazo. No perder cambios ni presentar documentos corruptos como catálogos vacíos. Documentar mecanismo y límites elegidos.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
