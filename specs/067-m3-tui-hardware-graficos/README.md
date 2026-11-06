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

## Base entregada en 066

La [066](../066-m3-tui-base/README.md#estado-y-evidencia) ya entrega barra común,
gráficos acotados de CPU, RAM, GPU y VRAM, numeración por adaptador y un
componente reutilizable de dos líneas. Sistema muestra el inventario y el
sondeo; la barra y los gráficos de GPU usan los adaptadores correlacionados con
CUDA o Vulkan. Esta tarjeta refina y acredita esa base.

## Trabajo y aceptación

- [ ] Alinear visualmente muestras de recursos con fases; conservar fecha/resolución efectiva sin afirmar causalidad o consumo exclusivo de un preset.
- [ ] Detallar múltiples GPU, RAM/VRAM y alcance proceso/máquina/dispositivo; no sumar ni atribuir varias veces recursos compartidos.
- [ ] Refinar ejes, escalas, leyendas y detalle de fuente/unidad/ventana. Separar cadencia de refresco y muestreo; señalar vigencia, huecos y datos ausentes sin interpolar ni mostrar ceros falsos.
- [ ] Verificar que la barra y las series comparten muestras y conservan continuidad al cambiar de espacio, servicio o panel. Aislar fuentes lentas y fallos por instancia.
- [ ] Revisar terminal grande y compacta, resize y lectura sin color con múltiples dispositivos, permisos insuficientes y proveedores ausentes o lentos. Medir cotas de captura y memoria.
- [ ] Repetir en Native AOT con cifras reales y contrastar fuente, alcance y disponibilidad de cada medición.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [063: Consultar servicios y hardware con métricas atribuibles y salida finita](../063-m3-estado-metricas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin sumar totales compartidos como consumo de cada servicio, benchmark ni guardar series históricas fuera de la sesión.

## Estado y evidencia

Planificada. El hardware y los gráficos básicos tienen evidencia en 066. Quedan por acreditar las ampliaciones y validaciones de esta tarjeta.
