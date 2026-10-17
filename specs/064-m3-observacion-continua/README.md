---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 063-m3-estado-metricas
created_at: 2026-10-21T23:26:06.353833100Z
updated_at: 2026-10-21T23:26:06.353833100Z
---

# Observar con ps -w o --watch y reconciliar eventos

## Objetivo

Añadir observación continua explícita y transporte reutilizable mientras el observador permanezca abierto.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [comandos.md](../../docs/comandos.md)
- [ui-servicio.md](../../docs/ui-servicio.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar ps -w y ps --watch como flags equivalentes, no subcomando ni comando independiente; sin flag conservar salida finita.
- [ ] Redibujar terminal y tratar salida redirigida conforme al contrato; cancelar cierra sólo el observador.
- [ ] Suscribir eventos soportados y reconciliar snapshot al conectar/reconectar, sin asumir replay durable o campos universales de forks.
- [ ] Aplicar reconexión con preferencias globales y distinguir cambio de identidad, desconexión y terminación; no reiniciar para reconectar.
- [ ] Separar frecuencia de fuente y refresco visual, mostrar cambios externos y evitar conversaciones privadas o instrumentación que genere inferencia.
- [ ] Probar pérdida de conexión, eventos desconocidos, cambio de proceso y ausencia de fuentes; exponer componentes reutilizables por dashboard.

## Dependencias

- [063: Consultar servicios y hardware con métricas atribuibles y salida finita](../063-m3-estado-metricas/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin servicio residente, streams privados, benchmarking o nuevas políticas de ejecución.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
