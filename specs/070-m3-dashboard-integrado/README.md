---
status: planned
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 055-m3-publicacion-presets
- 067-m3-tui-hardware-graficos
- 068-m3-tui-prompts-tokens
- 069-m3-tui-logs
created_at: 2026-10-21T23:26:07.035047300Z
updated_at: 2026-11-03T20:18:55.158104100Z
---
# Integrar y acreditar el dashboard Servicios completo

## Objetivo

Entregar una pantalla semigráfica de terminal utilizable para vigilar el servidor y sus presets durante inferencia real.

## Fuente de verdad

- [Milestone M4](../../docs/milestones/M4-experiencia-completa.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)
- [interfaces-visuales.md](../../docs/design/interfaces-visuales.md)
- [observabilidad.md](../../docs/design/observabilidad.md)
- [tui-servicio.salt.puml](../../docs/design/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Integrar actividad y series temporales arriba, hardware compartido y logs abajo. Verificar orden de pestañas y acceso a Logs dentro del servidor al reducir la altura.
- [ ] Partir de un proyecto con target self y varios presets; iniciar/publicar mediante CLI y abrir tui. Enviar peticiones desde un cliente de prueba y observar las fases y cifras disponibles mientras ocurre inferencia.
- [ ] Repetir con servicio compartido por consumidores, dos entradas de modelos distintos usando recursos acordados y actividad simultánea cuando el backend lo soporte; verificar correlación y ausencia de métricas mezcladas.
- [ ] Probar cambio de preset/entrada observado, recarga de publicación, reinicio del servidor, reconexión y telemetría parcial; mantener información vigente y distinguir incertidumbre.
- [ ] Comprobar resize, teclado, foco, scroll, falta de color, terminal pequeña, buffers acotados y aislamiento de errores; mostrar identidad, fase/progreso y cifras aun al simplificar gráficos.
- [ ] Registrar evidencia visual de la aplicación Native AOT en terminal real, pasos reproducibles y comparación con fuentes. Capturas estáticas o dashboard sin actividad no acreditan el recorrido.
- [ ] Comprobar que cerrar la TUI deja el servicio atendiendo y no modifica intención ni publicación.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)
- [067: Mostrar hardware y gráficas temporales legibles en la terminal](../067-m3-tui-hardware-graficos/README.md)
- [068: Visualizar prompts, generación de tokens y tareas de cada preset](../068-m3-tui-prompts-tokens/README.md)
- [069: Integrar logs continuos con selección de servicio y navegación](../069-m3-tui-logs/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Dashboard de observación de M4. Los editores, la experimentación asistida y la Web UI tienen recorridos propios dentro del milestone.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
