---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 054-m3-recuperacion
created_at: 2026-10-21T23:26:06.006135900Z
updated_at: 2026-10-21T23:26:06.006135900Z
---

# Configurar logs y notificar fallos de inicio tras recuperación

## Objetivo

Conservar diagnóstico de cada ejecución y emitir sólo las notificaciones configuradas.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md#hook-de-fallo-de-inicio](../../docs/servidores.md#hook-de-fallo-de-inicio)
- [observabilidad.md#logs](../../docs/observabilidad.md#logs)
- [formato-proyecto.md](../../docs/formato-proyecto.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver destinos globales/propios/externos y nombres por instancia; default pertenece al estado del servicio, no al executable externo.
- [ ] Aplicar retención global y override del servicio, protegiendo activo/último y mostrando excesos de protegidos sin prometer cuota estricta.
- [ ] Implementar service.server.hooks.start-failed command/args, contexto por stdin y plazo conforme al canónico; no ejecutar shell ni filtrar secretos.
- [ ] Notificar una vez después del intento de recuperación, incluso cuando restaura servicio; distinguir candidata, resultado de recuperación y disponibilidad final.
- [ ] Probar fallo de hook sin revertir recuperación, integridad fatal, doble fallo, primer arranque y ausencia de notificación configurada.

## Dependencias

- [054: Recuperar snapshots funcionales sin reinicios implícitos de consumidores](../054-m3-recuperacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor residente, crear usuarios/privilegios o implementar seguimiento continuo/historial, que corresponde a 062.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
