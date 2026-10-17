---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
- 052-m3-evidencia-router
created_at: 2026-10-21T23:26:05.397992100Z
updated_at: 2026-10-21T23:26:05.397992100Z
---

# Registrar servicios y operar su proceso con identidad segura

## Objetivo

Entregar registro, selección y ciclo de servicios globales y propios sin acoplarlos a la publicación de un consumidor.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md](../../docs/servidores.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar service/server list, selección explícita y asignación de default como alias según la gramática canónica; resolver nombre raíz del único service y target self/default/nombrado según contexto.
- [ ] Implementar start/stop/restart, foreground/desacoplado y una instancia por servicio; no reasignar puertos ni iniciar sustitutos ante error.
- [ ] Verificar proceso/endpoint y registrar snapshot/recursos/log; API inaccesible no prueba terminación ni autoriza actuar sobre un PID reutilizado.
- [ ] Preparar antes de detener en restart y conservar activo si preparación falla; validar fallos conocidos sin auditoría completa de pesos por inicio.
- [ ] Comprobar salud configurada y promover última buena elegible sólo con evidencia local; respetar plazos y reglas de parada del contrato.
- [ ] Implementar baja de servicio detenido conservando archivos/logs; rechazar referencias activas o identidad incierta y retirar alias default sin elegir reemplazo.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)
- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No implementa up/down ni publica presets de consumidores. La recuperación completa se entrega en 054; no añade supervisor ni servicio del SO.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
