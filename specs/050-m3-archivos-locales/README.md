---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 049-m3-modelos-companions
created_at: 2026-10-21T23:26:05.178306100Z
updated_at: 2026-10-21T23:26:05.178306100Z
---

# Reutilizar pesos locales y resolver referencias tipadas sin redescarga

## Objetivo

Usar recursos aportados por el usuario y resolver referencias de argumentos conservando ubicación y propiedad.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Registrar principales/companions y shards locales en el ámbito seleccionado; usar la copia actual, incluso si proviene de otro gestor.
- [ ] Resolver model:// y companion:// en args a la ruta de entrada de la unidad, con literal explícito y error por referencia inexistente.
- [ ] Persistir alta antes del --try-match opcional; fallo remoto, cancelación o falta de interacción conservan registro y uso sin certificar procedencia.
- [ ] Conservar externos dentro/fuera de base sin mover, adoptar o borrar bytes; referencias relativas usan la base canónica.
- [ ] Mantener transición externo a gestionado sólo explícita, verificada y sin destruir originales o referencias activas.
- [ ] Probar copia local, partes incompletas, traslado de proyecto y base de origen de referencias externas durante importación.

## Dependencias

- [049: Resolver inventario y adquisición de modelos y companions por ámbito](../049-m3-modelos-companions/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin escanear otras herramientas, búsqueda visual ni ejecutar la operación de importación global, que corresponde a 056.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
