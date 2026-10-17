---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- tui
- dashboard
depends_on:
- 065-m3-actividad-tareas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.862426300Z
updated_at: 2026-10-21T23:26:06.862426300Z
---

# Visualizar prompts, generación de tokens y tareas de cada preset

## Objetivo

Entregar el panel central donde se vea la actividad de llama-server por servicio y entrada, desde prompt hasta salida.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [tui-servicio.salt.puml](../../docs/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Mostrar tabla central con servicio, entrada/preset y modelo correlacionado, slot, tarea y fase; conservar estas identidades al navegar y distinguir instancias.
- [ ] Visualizar procesamiento de prompt con tokens procesados/cacheados y progreso disponible, tiempo de fase, generación con tokens de salida y tasa etiquetada; mostrar curvas de prompt/generación según fuentes acreditadas.
- [ ] Diferenciar cola, prompt, generación, finalización y fallo cuando estén observados; preservar cifras desconocidas/no disponibles y su motivo, sin deducir inactividad de silencio de logs.
- [ ] Mostrar tareas simultáneas y finalizadas con histórico configurable, scroll y último valor observado; el límite sólo afecta finalizadas. Mantener activas visibles y diferenciadas.
- [ ] Adaptar columnas y gráficos al ancho/alto sin perder servicio, entrada, fase ni progreso disponible. Ampliar logs o pausar visualización no detiene la captura de actividad.
- [ ] Acreditar con peticiones reales de prueba la transición prompt→generación→fin, dos presets/entradas y cambio de instancia; contrastar cifras con la fuente. No basta animar fixtures.

## Dependencias

- [065: Correlacionar actividad de prompts y generación por entrada, slot y tarea](../065-m3-actividad-tareas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin visor del texto de conversaciones, botones para cargar modelos ni promesa de granularidad que el build no exponga. Los límites de fuente se muestran y se documentan para revisión.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
