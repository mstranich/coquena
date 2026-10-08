---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:16.165707400Z
updated_at: 2026-10-07T22:02:16.165707400Z
---

# Selección Hugging Face y menú de descarga

## Objetivo
Adaptador de metadatos públicos: resolver commit, inventario paginado, variantes y unidad completa de shards. Implementar menú según cuantización indicada o ausente, README bajo demanda y cancelación. Producir selección fijada para el consumidor, sin descargar pesos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [ ] Fixtures prueban paginación, revisión inexistente, rate limit, variantes ambiguas/shard faltante y README del mismo commit. Cancelar no crea GUID; elegir variante inicia sin segunda confirmación. No clonar ni recorrer historial; verificar API oficial vigente al implementar.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
