---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:17.123689500Z
updated_at: 2026-10-07T22:02:17.123689500Z
---

# Inspección GGUF y coherencia de shards

## Objetivo
Implementar lectura acotada de encabezados y agrupación estándar, completitud y coherencia estructural del conjunto. Proveer SHA-256 de bytes completo y resultado de integridad, separados de identidad/revisión remota.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [ ] Fixtures de un archivo y múltiples shards: faltantes, duplicados, encabezado truncado, metadatos contradictorios y digest incorrecto. Hash incremental con memoria acotada y prefijo reanudado cubierto. No certificar compatibilidad de inferencia ni tratar commit/ETag como SHA de pesos.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
