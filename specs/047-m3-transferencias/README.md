---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 042-unidades-legibles
- 044-m3-contextos-persistencia
created_at: 2026-10-21T23:26:04.956876600Z
updated_at: 2026-10-21T23:26:04.956876600Z
---

# Aislar transferencias y publicación física por ámbito

## Objetivo

Reutilizar las garantías de transferencia M2 con operaciones, preparación y propiedad aisladas por ámbito.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [descargas.md](../../docs/descargas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver download y GUID/checkpoints en el ámbito seleccionado; impedir ejecutores duplicados y efectos sobre otro inventario.
- [ ] Conservar selección/commit, rangos y continuidad verificada, pausa, reanudación y cancelación sólo de preparación propia.
- [ ] Verificar unidades completas y artefactos de backend antes de publicar, preservando revisiones activas y recursos externos.
- [ ] Separar publicación física de instalaciones del commit de resoluciones de una combinación; exponer progreso humano legible y datos exactos estructurados.
- [ ] Probar contención, interrupciones, cambio remoto, fallos de extracción/integridad/publicación y recuperación sin adopción de bytes no acreditados.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin scheduler residente, activación de procesos ni publicación parcial de un upgrade conjunto.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
