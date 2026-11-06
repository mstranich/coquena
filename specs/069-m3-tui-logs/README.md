---
status: planned
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 062-m3-seguimiento-logs
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.949521100Z
updated_at: 2026-11-03T20:18:55.155643Z
---
# Integrar logs continuos con selección de servicio y navegación

## Objetivo

Centralizar logs debajo de hardware y actividad con continuidad por identidad de servicio.

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

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Usar seguimiento de 062: pestaña de servidor y alias default, cambio de log entre reinicios/rotación y señal visible de transición; esperar cuando el servicio esté detenido.
- [ ] Cambiar de pestaña cambia actividad y logs al mismo servicio; el hardware conserva alcance global.
- [ ] Permitir scroll, pausa visual, regreso al seguimiento y acceso a históricos; continuar captura en buffers acotados sin congelar tablas ni peticiones.
- [ ] Destinar al log el espacio restante y moverlo a una subpestaña del mismo servidor cuando falte altura, conservando fase y progreso en Monitor; soportar líneas largas, Unicode, ausencia de archivo y pérdida de acceso.
- [ ] Probar dos servicios, ráfagas de logs, resize, retención, reinicio y eliminación/recreación del mismo nombre con otra identidad; no enlazar silenciosamente servicios distintos.
- [ ] Ejecutar la TUI publicada con logs reales y comprobar que salida/pausa no detienen el backend ni provocan crecimiento ilimitado.

## Profundización del visor de logs existente

066 ya permite consultar, recorrer y pausar el log dentro de la TUI y volver al
seguimiento. Esta tarjeta completa históricos y endurece continuidad/retención.

- [ ] Seleccionar históricos por instancia/archivo y volver inequívocamente al servicio actual, distinguiendo histórico, pausa visual, espera y desconexión.
- [ ] Conservar posición/foco de lectura al llegar líneas; comunicar descartes del buffer si corresponde sin prometer conservación ilimitada.
- [ ] Validar rotación, truncado, eliminación por retención, ráfagas y pérdida de acceso sin congelar gráficas ni hardware.
- [ ] Revisar panel compartido y subpestaña en terminal baja; la identidad del servidor permanece visible. No sustituir el monitor por una consola textual.

## Dependencias

- [062: Seguir logs por identidad de servicio entre reinicios y rotación](../062-m3-seguimiento-logs/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin mezclar logs de distintas instancias como una sola ejecución ni modificar configuración del servicio al navegar.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
