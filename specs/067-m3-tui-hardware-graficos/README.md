---
status: planned
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 042-unidades-legibles
- 063-m3-estado-metricas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.764688800Z
updated_at: 2026-11-03T20:18:55.151265900Z
---
# Mostrar hardware y gráficas temporales legibles en la terminal

## Objetivo

Hacer visible consumo de CPU, RAM y cada GPU/VRAM con cifras y gráficas que mantengan su alcance y disponibilidad.

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
- [glosario.md](../../docs/design/glosario.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Alimentar la barra de estado común con las mismas muestras de hardware que el monitor. Verificar continuidad al cambiar de espacio y pestaña, identificación de GPU y degradación por ancho o fuente ausente según [interfaces visuales](../../docs/design/interfaces-visuales.md#barra-de-estado-de-la-máquina).

- [ ] Mostrar hardware compartido en la región superior del monitor, por dispositivo, con fuente, unidad, cifra y serie temporal; distinguir consumo por proceso de totales globales sin duplicar GPU compartida.
- [ ] Representar historial acotado de sesión con caracteres de terminal Unicode/braille/ASCII, ejes o etiquetas legibles y formato humano de 042. Dato ausente no es cero.
- [ ] Separar refresco visual de referencia de un segundo del ritmo real de muestreo; marcar muestras desactualizadas y huecos en lugar de interpolar actividad inexistente.
- [ ] Mantener hardware global al cambiar de pestaña; la vista aísla fallos por instancia y no bloquea toda la pantalla por una fuente lenta.
- [ ] Probar CPU/GPU sin permisos, múltiples GPU, falta de proveedor, terminal pequeña, resize y uso sin color. Medir límites de buffers y comprobar que el observador no consume recursos crecientes.
- [ ] Publicar Native AOT y observar cifras reales del equipo; contrastar alcance con la fuente y documentar qué mediciones están disponibles.

## Profundización de hardware y gráficas existentes

066 ya muestra consumo y series junto a inferencia/logs. Esta tarjeta amplía
cobertura y legibilidad, sin entregar por primera vez las curvas esenciales.

- [ ] Alinear visualmente muestras de recursos con fases; conservar fecha/resolución efectiva sin afirmar causalidad o consumo exclusivo de un preset.
- [ ] Detallar múltiples GPU, RAM/VRAM y alcance proceso/máquina/dispositivo; no sumar ni atribuir varias veces recursos compartidos.
- [ ] Refinar ejes, escalas, leyendas y detalle de fuente/unidad/ventana; pico observado y huecos conservan la semántica de 066.
- [ ] Mantener continuidad de barra/series en la navegación ya implementada, sin construir herramientas nuevas para acreditar el cambio.
- [ ] Revisar UX en terminal grande/compacta con fuentes lentas y múltiples dispositivos; medir cotas de memoria/captura y documentar límites de escala comprobados.

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
