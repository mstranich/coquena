---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- tui
- dashboard
depends_on:
- 042-unidades-legibles
- 063-m3-estado-metricas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.764688800Z
updated_at: 2026-10-21T23:26:06.764688800Z
---

# Mostrar hardware y gráficas temporales legibles en la terminal

## Objetivo

Hacer visible consumo de CPU, RAM y cada GPU/VRAM con cifras y gráficas que mantengan su alcance y disponibilidad.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [glosario.md](../../docs/glosario.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Mostrar hardware de máquina arriba, por dispositivo, con fuente, unidad, cifra y serie temporal; distinguir consumo por proceso de totales globales sin duplicar GPU compartida.
- [ ] Representar historial acotado de sesión con caracteres de terminal Unicode/braille/ASCII, ejes o etiquetas legibles y formato humano de 042. Dato ausente no es cero.
- [ ] Separar refresco visual de referencia de un segundo del ritmo real de muestreo; marcar muestras desactualizadas y huecos en lugar de interpolar actividad inexistente.
- [ ] Mantener hardware global al cambiar servicio o log; resumen general aísla fallos por instancia y no bloquea toda la pantalla por una fuente lenta.
- [ ] Probar CPU/GPU sin permisos, múltiples GPU, falta de proveedor, terminal pequeña, resize y uso sin color. Medir límites de buffers y comprobar que el observador no consume recursos crecientes.
- [ ] Publicar Native AOT y observar cifras reales del equipo; contrastar alcance con la fuente y documentar qué mediciones están disponibles.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [063: Consultar servicios y hardware con métricas atribuibles y salida finita](../063-m3-estado-metricas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin sumar totales compartidos como consumo de cada servicio, benchmark ni guardar series históricas fuera de la sesión.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
