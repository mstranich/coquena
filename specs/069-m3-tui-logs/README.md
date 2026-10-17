---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- tui
- dashboard
depends_on:
- 062-m3-seguimiento-logs
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.949521100Z
updated_at: 2026-10-21T23:26:06.949521100Z
---

# Integrar logs continuos con selección de servicio y navegación

## Objetivo

Centralizar logs debajo de hardware y actividad con continuidad por identidad de servicio.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)
- [observabilidad.md](../../docs/observabilidad.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Usar seguimiento de 062: selector de servicio y alias default, cambio de log entre reinicios/rotación y señal visible de transición; esperar cuando el servicio esté detenido.
- [ ] Cambiar selección afecta sólo el panel de logs, no filtra implícitamente hardware global ni actividad general.
- [ ] Permitir scroll, pausa visual, regreso al seguimiento y acceso a históricos; continuar captura en buffers acotados sin congelar tablas ni peticiones.
- [ ] Destinar al log el espacio restante y permitir ajustar paneles conservando fase y progreso; soportar líneas largas, Unicode, ausencia de archivo y pérdida de acceso.
- [ ] Probar dos servicios, ráfagas de logs, resize, retención, reinicio y eliminación/recreación del mismo nombre con otra identidad; no enlazar silenciosamente servicios distintos.
- [ ] Ejecutar la TUI publicada con logs reales y comprobar que salida/pausa no detienen el backend ni provocan crecimiento ilimitado.

## Dependencias

- [062: Seguir logs por identidad de servicio entre reinicios y rotación](../062-m3-seguimiento-logs/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin mezclar logs de distintas instancias como una sola ejecución ni modificar configuración del servicio al navegar.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
