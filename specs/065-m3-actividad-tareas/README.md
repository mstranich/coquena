---
status: planned
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 052-m3-evidencia-router
- 064-m3-observacion-continua
created_at: 2026-10-21T23:26:06.451582300Z
updated_at: 2026-11-03T20:18:55.143585400Z
---
# Correlacionar actividad de prompts y generación por entrada, slot y tarea

## Objetivo

Reconstruir actividad observable del servidor para que la TUI muestre qué trabaja cada entrada, sin atribuciones inventadas.

## Acuerdo de producto y continuidad entre sesiones

M4 es un milestone abierto, centrado en una Terminal UI con buena experiencia de
usuario. Se agregan, corrigen y refinan tarjetas una por una a partir del uso y la
revisión visual. Las tarjetas actuales no definen un inventario cerrado ni su
finalización implica cerrar M4. Cada tarjeta mantiene un alcance concreto; el
horizonte abierto no autoriza implementar otras secciones sin acordarlas.

La primera entrega utilizable es [066](../066-m3-tui-base/README.md): observar
inferencia real con fases, avance disponible hacia la respuesta, curvas de tokens,
promedio/pico, consumo y logs dentro de la terminal. Leer su intención, matriz de
fuentes y aceptación antes de implementar esta tarjeta. Una carcasa de navegación,
hardware aislado o un indicador binario de actividad no satisfacen ese recorrido.

Se comienza por 066; 065, 067, 068 y 069 amplían una experiencia ya utilizable;
070 contrasta su integración. Los IDs/directorios conservan nombres históricos,
no fijan el orden. No esperar a 070 para integrar datos o probar inferencia real.
La Web UI será otra iteración: lo aprendido de UX y los contratos sin widgets
servirán de base, sin implementar ahora web ni copiar literalmente su layout.

## Fuente de verdad

- [Milestone M4](../../docs/milestones/M4-experiencia-completa.md)
- [observabilidad.md](../../docs/design/observabilidad.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)
- [interfaces-visuales.md](../../docs/design/interfaces-visuales.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Base entregada y reparto con 068

La [066](../066-m3-tui-base/README.md#primera-evidencia-de-curvas-de-inferencia)
acreditó lectura de slots, identidad del hijo y tasas por fase para una tarea
activa de un preset en el build probado. Ya separa los intervalos de prompt y
generación y corta las tasas ante huecos o cambio de identidad. El adaptador
base sólo calcula deltas cuando hay una tarea activa atribuible.

La 065 es dueña de la identidad, la atribución y el modelo de lectura de
tareas activas y finalizadas, incluido el límite de retención de finalizadas.
La [068](../068-m3-tui-prompts-tokens/README.md) consume ese modelo para
presentar, navegar y comparar tareas. La integración en 065 verifica la
atribución en el monitor existente.

## Trabajo y aceptación

- [ ] Ampliar la matriz acreditada en 052 y 066 con el seguimiento de 064. Contrastar endpoints, eventos, logs y stream propio por build, incluido el momento de actualización de cada contador.
- [ ] Correlacionar presets distintos con mismos pesos, tareas simultáneas, IDs reutilizados, tareas sin slot, huecos, cancelación y reemplazo del hijo sin mezclar promedios/picos entre generaciones.
- [ ] Distinguir cola, prompt, generación, finalización, fallo y estados desconocidos con evidencia. La cola agregada no crea filas individuales. No atribuir proyecto o cliente por el nombre de la entrada.
- [ ] Conservar duración, progreso disponible, tokens/caché/tasas y sus ventanas atribuibles. Separar acumulados y buckets por hijo de cifras por tarea, promedio y pico observado. Un puerto, slot o ID de tarea aislado no acredita identidad. Validar la semántica del denominador antes de calcular porcentajes.
- [ ] Conservar las últimas cifras observadas de tareas finalizadas en un histórico de sesión con límite configurable, independiente de las tareas activas, la capacidad del backend y la retención de logs.
- [ ] Separar razonamiento de salida visible sólo con evidencia atribuible; no deducirlo de silencio ni leer conversaciones ajenas.
- [ ] Probar el modelo reutilizable sin widgets con concurrencia, eventos repetidos/desordenados, huecos, cancelación, fallos, reinicio e IDs reutilizados. Contrastar fixtures con peticiones reales de varios presets e integrar el resultado en el monitor existente.

## Dependencias

La integración de esta ampliación requiere el monitor de [066](../066-m3-tui-base/README.md).
`leanspec rel add` no está migrado al adaptador local: la relación se declara
aquí y queda por reconciliar en metadatos mediante CLI, sin edición manual.


- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)
- [064: Observar con ps -w o --watch y reconciliar eventos](../064-m3-observacion-continua/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin inferir contenido privado, crear un scheduler, controlar residencia de modelos ni introducir una plataforma histórica persistente.

## Estado y evidencia

Planificada. La evidencia de una tarea activa pertenece a 066. Falta ejecutar y acreditar la ampliación de correlación descrita aquí.
