---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- tui
- dashboard
depends_on:
- 044-m3-contextos-persistencia
- 052-m3-evidencia-router
created_at: 2026-10-21T23:26:06.576440300Z
updated_at: 2026-10-21T23:26:06.576440300Z
---

# Abrir la interfaz de terminal y navegar entre servicios conocidos

## Objetivo

Entregar la base ejecutable de la TUI, su navegación y ciclo de vida para integrar el dashboard como producto de M3.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [tui-servicio.salt.puml](../../docs/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Seleccionar una biblioteca de terminal tras comprobar publicación y ejecución Native AOT win-x64 con teclado, Unicode, resize y restauración de terminal; registrar versión y limitaciones antes de adoptarla.
- [ ] Exponer la entrada tui definida por comandos; abrir desde un proyecto y navegar entre resumen general y servicio seleccionado, global o propio, usando registro local sin recorrer discos.
- [ ] Preparar regiones de hardware, actividad y logs conforme al wireframe Servicios; los paneles de datos se completan en 067–069. Mostrar identidad y estado de selección aun sin servidor.
- [ ] Resolver foco, navegación por teclado, salida, redimensionado y terminal pequeña conservando información prioritaria; mantener alternativa comprensible sin color.
- [ ] Abrir/cerrar no inicia, detiene, publica ni reconfigura servicios. Cancelar observadores restaura la terminal y libera suscripciones sin afectar backend.
- [ ] Validar la aplicación publicada en una terminal real, incluida salida tras excepción; las pruebas de layout aislado no acreditan esta entrega por sí solas.

## Dependencias

- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)
- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin editores, Web UI, búsqueda visual, controles de ejecución ni pestañas vacías presentadas como funciones implementadas. La base sola no acredita el dashboard.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
