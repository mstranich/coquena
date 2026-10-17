---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 042-unidades-legibles
- 053-m3-servicios
created_at: 2026-10-21T23:26:06.261807300Z
updated_at: 2026-10-21T23:26:06.261807300Z
---

# Consultar servicios y hardware con métricas atribuibles y salida finita

## Objetivo

Entregar ps y snapshots reutilizables con fuente, unidad y disponibilidad sin efectos sobre el backend.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Consultar todos los servicios conocidos o el seleccionado, globales y propios, verificando identidad/proceso/API/salud y aislando fallos.
- [ ] Mostrar composición realmente activa frente a intención actual, incluido fallback y estado local de nombres; no inferirla del YAML editado.
- [ ] Obtener CPU/RAM y GPU/VRAM con alcance comprobado por proceso/dispositivo/global; no atribuir totales compartidos a cada servicio ni inventar cero.
- [ ] Consultar métricas/eventos disponibles sin cargas, recargas o reinicios por observar; mostrar memoria y tamaños con el formato único de 042.
- [ ] Probar proceso vivo sin API, PID reutilizado, backend sin telemetría y recursos compartidos; exponer snapshots de dominio para CLI/TUI.
- [ ] Exponer muestras fechadas y su vigencia, con acceso acotado por instancia y fallo aislado; la reconstrucción de fases/tareas se implementa en 065.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [053: Registrar servicios y operar su proceso con identidad segura](../053-m3-servicios/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Ps sin opciones termina tras la instantánea. Sin benchmark, control de modelos, supervisor o componentes visuales.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
