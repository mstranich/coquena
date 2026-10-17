---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
created_at: 2026-10-21T23:26:05.322040800Z
updated_at: 2026-10-21T23:26:05.322040800Z
---

# Acreditar capacidades del build oficial para presets y observación

## Objetivo

Fijar evidencia ejecutada del build/plataforma que respalda el adaptador, subordinada al contrato funcional aprobado.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md](../../docs/servidores.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Fijar build y demostrar arranque vacío, codec INI, precedencia y recarga de altas/cambios/retiradas de presets con nombres válidos.
- [ ] Comprobar publicaciones de varios consumidores y el resultado de una recarga con peticiones en curso, sin prometer políticas propias de ocupación o atomicidad remota.
- [ ] Acreditar restauración acotada de entradas afectadas y diagnóstico de reinicio explícito necesario sin ejecutarlo automáticamente.
- [ ] Contrastar health, warmup declarado, SSE, métricas, slots y logs, con atribución disponible por servicio/instancia/entrada/tarea y ausencia de cargas por observar.
- [ ] Distinguir aceptación, resultado confirmado e incertidumbre; documentar límites y diagnóstico de capacidades ausentes sin redefinir la política funcional.
- [ ] Usar fixtures o recursos existentes; lectura de master no acredita soporte del build ni autoriza adoptar dependencias.
- [ ] Construir una matriz de cada cifra y fase del dashboard con fuente pública o log reconocido, campo, unidad, ventana, granularidad, build y fixture real. Cubrir prompt/cache/progreso, salida/tokens por segundo, cola, finalización, slot/tarea y entrada/modelo.
- [ ] Ejecutar peticiones de prueba de prompt y generación sobre dos presets, preferentemente de modelos distintos con recursos acordados; capturar snapshots y logs correlacionables sin registrar conversaciones privadas. Verificar concurrencia y reutilización de IDs tras reinicio.
- [ ] Evaluar explícitamente qué puede observarse a tiempo real y qué sólo al terminar. Una capacidad ausente debe tener diagnóstico y representación visible; no acreditar un dashboard funcional sólo con fixtures ni asumir que SSE contiene todos los datos.

## Dependencias

Sin bloqueos de implementación dentro de M3. Referenciar documentación no crea una dependencia.

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor, gestión propia de modelos residentes, adaptadores universales de forks ni descargas costosas implícitas. Es evidencia del backend, no implementación anticipada de las otras tarjetas.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
