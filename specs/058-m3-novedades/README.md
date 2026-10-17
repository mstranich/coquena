---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
created_at: 2026-10-21T23:26:05.758536300Z
updated_at: 2026-10-21T23:26:05.758536300Z
---

# Consultar novedades y persistir planes exactos por selección

## Objetivo

Informar novedades globales o de combinación y conservar un plan explicable sin modificar intención o pesos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Consultar origen y fijar versiones/commits/artefactos actuales y propuestos por recurso, sin descargar pesos ni actualizar otras combinaciones.
- [ ] Mantener plan local ligado a intención/resolución/revisión y fecha; edición concurrente o cambio de intención invalida el plan.
- [ ] Respetar cuantización, variante, propiedad y correspondencia exacta; fallo remoto o no concluyente no equivale a sin novedades.
- [ ] Informar novedades aun con pin fijo sin convertirlo a latest; comprobación automática sólo informa, sin upgrade ni reinicio.
- [ ] Implementar selectores de recurso y probar candidato presente, renombres remotos, ambigüedad, error y ausencia real.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin publicar recursos, editar YAML ni recalcular y aplicar silenciosamente otro plan.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
