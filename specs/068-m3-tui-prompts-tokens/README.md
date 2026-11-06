---
status: planned
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 065-m3-actividad-tareas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.862426300Z
updated_at: 2026-11-03T20:18:55.153264700Z
---
# Visualizar prompts, generación de tokens y tareas de cada preset

## Objetivo

Entregar el panel central donde se vea la actividad de llama-server por servicio y entrada, desde prompt hasta salida.

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
- [ui-servicio.md](../../docs/design/ui-servicio.md)
- [interfaces-visuales.md](../../docs/design/interfaces-visuales.md)
- [observabilidad.md](../../docs/design/observabilidad.md)
- [tui-servicio.salt.puml](../../docs/design/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Priorizar las entradas activas y sus series temporales arriba del log. Mostrar servicio, entrada/preset y modelo correlacionado, slot, tarea y fase; conservar estas identidades al navegar y distinguir instancias.
- [ ] Separar resumen por entrada (acumulados, medias del backend, actividad/cola agregada y período) del detalle por tarea, según el Salt actualizado y observabilidad. Mostrar totales compatibles sin sumar medias como velocidad instantánea; señalar huecos y ausencia de fuentes por tarea. Distinguir generaciones del hijo e IDs reutilizados.
- [ ] Visualizar procesamiento de prompt con tokens procesados/cacheados y progreso disponible, tiempo de fase, generación con tokens de salida y tasa etiquetada; mostrar curvas de prompt/generación según fuentes acreditadas.
- [ ] Diferenciar cola, prompt, generación, finalización y fallo cuando estén observados; preservar cifras desconocidas/no disponibles y su motivo, sin deducir inactividad de silencio de logs.
- [ ] Mostrar tareas simultáneas y finalizadas con histórico configurable, scroll y último valor observado; el límite sólo afecta finalizadas. Mantener activas visibles y diferenciadas.
- [ ] Adaptar columnas y gráficos al ancho/alto sin perder servicio, entrada, fase ni progreso disponible. Ampliar logs o pausar visualización no detiene la captura de actividad.
- [ ] Acreditar con peticiones reales de prueba la transición prompt→generación→fin, dos presets/entradas y cambio de instancia; contrastar cifras con la fuente. No basta animar fixtures.

## Profundización de inferencias, comparación visual e histórico

066 ya muestra fases, curvas, avance, promedio y pico disponibles. Aquí se amplían
detalle, concurrencia e histórico; no se posterga hasta esta tarjeta la primera
observación de tokens o una inferencia real.

- [ ] Separar resumen de entrada de detalle por tarea y cola agregada; no duplicar tasas ni inventar tareas por un contador.
- [ ] Ampliar histórico configurable de finalizadas con scroll/selección estable sin expulsar actividad actual ni limitar ejecución.
- [ ] Revisar ejecuciones recientes de presets del mismo modelo con distinta configuración, incluida especulación cuando sea viable. Mostrar configuración activa, caché, ventana y condiciones que impiden comparación directa.
- [ ] Dar acceso a tiempos, promedio y pico observado/máximo de medias sin sobrecargar vista compacta. No representar máximo de tokens como longitud final ni prometer mejora por usar borrador.
- [ ] Probar varias peticiones reales y cambio del hijo manteniendo log/hardware usables; no implementar un sistema formal de benchmarks por esta tarjeta.

## Dependencias

- [065: Correlacionar actividad de prompts y generación por entrada, slot y tarea](../065-m3-actividad-tareas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin visor del texto de conversaciones, botones para cargar modelos ni promesa de granularidad que el build no exponga. Los límites de fuente se muestran y se documentan para revisión.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
