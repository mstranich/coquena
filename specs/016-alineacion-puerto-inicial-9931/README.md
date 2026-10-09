---
status: planned
created: 2026-10-09
tags:
- m1
depends_on:
- '011'
created_at: 2026-10-09T13:41:51.558051500Z
updated_at: 2026-10-09T13:41:51.558051500Z
---

# Alinear el puerto inicial de servidores con llama.cpp: 9931

## Objetivo
Cambiar el puerto inicial de Coquena de 8080 a 9931 para nuevas definiciones con catálogo vacío, alineándolo al cambio anunciado por llama.cpp. Mantener los puertos ya persistidos y las reglas de asignación y unicidad.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [Puertos y configuración de servidores](../../docs/servidores.md#puertos)
- [CLI de servidores](../../docs/comandos.md#servidores-e-instancias)
- [011 — Definiciones, defaults y puertos](../011-m1-definiciones-server/README.md)
- [Anuncio upstream: PR 26508](https://github.com/ggml-org/llama.cpp/pull/26508)

## Motivo
El 9 de octubre de 2026 el usuario probó manualmente `server start` y recibió el aviso de llama.cpp sobre el próximo cambio de puerto predeterminado a 9931. La PR enlazada anuncia esa transición; no demuestra por sí sola que todos los builds ya hayan cambiado el default. Coquena persiste y emite un puerto concreto, por lo que la alineación requiere un cambio propio, no esperar que el backend elija otro.

## Trabajo y aceptación
- [ ] Actualizar la regla canónica y sus diagramas/ejemplos para usar 9931 como puerto inicial con catálogo vacío y en la creación inicial de default.
- [ ] Actualizar implementación, descripción del parámetro port y pruebas pertinentes, manteniendo max+1 sobre los puertos guardados cuando hay servidores registrados.
- [ ] Probar catálogo nuevo/default inicial en 9931, creación posterior por max+1, selección explícita de otro puerto, colisión y overflow sin cambios parciales.
- [ ] Probar que catálogos existentes con 8080 u otros puertos conservan sus valores y que start/restart no migran ni reasignan puertos. Unset port mantiene su regla max+1 y no salta a 9931.
- [ ] Mantener emisión explícita de --port según la definición: sin dependencia del default de cada versión de llama.cpp, consultas de red ni resolución dinámica de puerto al iniciar.
- [ ] Ejecutar pruebas pertinentes, publicación/ejecución Native AOT win-x64, validación de documentación y git diff --check; registrar evidencia.

## Límites
Sólo el puerto inicial. Sin migración automática de servidores existentes, reasignación por puerto ocupado, cambios de host/metrics, daemon ni ampliación a M2. Registrar la tarjeta no autoriza ni completa su implementación. No bloquea las entregas 012–014.

## Evidencia
Pendiente de implementación. Fuente del aviso confirmada en la PR upstream al redactar esta tarjeta. La prueba manual de inicio/parada se registra como evidencia de 012, no como validación de este cambio.
