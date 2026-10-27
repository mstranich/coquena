---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- tui
- dashboard
depends_on:
- 052-m3-evidencia-router
- 064-m3-observacion-continua
created_at: 2026-10-21T23:26:06.451582300Z
updated_at: 2026-10-21T23:26:06.451582300Z
---

# Correlacionar actividad de prompts y generación por entrada, slot y tarea

## Objetivo

Reconstruir actividad observable del servidor para que la TUI muestre qué trabaja cada entrada, sin atribuciones inventadas.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Consumir las fuentes acreditadas en 052 y el seguimiento 064; identificar cada observación por servicio, instancia, entrada y slot/tarea disponibles. Correlacionar nombre publicado del preset y modelo sólo cuando exista evidencia.
- [ ] Distinguir cola, procesamiento de prompt, generación y finalización; admitir tarea sin slot y estados desconocidos. Un contador agregado de cola no crea filas individuales.
- [ ] Proporcionar tokens procesados/cacheados, progreso del prompt, duración de fase, tokens de salida y tasas con fuente, ventana y vigencia. Distinguir acumulados, media, pico de muestras y estimación de valores instantáneos.
- [ ] Conservar últimas cifras observadas al finalizar; histórico acotado de sesión con límite configurable de filas finalizadas, independiente de filas activas, capacidad del backend y retención de logs.
- [ ] Probar tareas simultáneas de presets distintos, eventos repetidos/desordenados, huecos de observación, cancelación/fallo, reinicio e IDs reutilizados. No atribuir proyecto o cliente por prefijo de entrada.
- [ ] Contrastar fixtures con una captura real de prompt y generación del build acreditado; exponer un modelo de lectura reutilizable por TUI sin dependencia de widgets.
- [ ] Separar tasas/buckets por hijo de cifras por petición y totales compatibles según observabilidad; tratar reemplazo del hijo como otra generación aun con router vivo. No usar puerto, tarea o slot aislado como identidad, ni n_prompt_tokens como denominador fijo sin semántica acreditada. Incorporar los contrastes de la matriz ejecutada de 052.

## Dependencias

- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)
- [064: Observar con ps -w o --watch y reconciliar eventos](../064-m3-observacion-continua/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin inferir contenido privado, crear un scheduler, controlar residencia de modelos ni introducir una plataforma histórica persistente.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
