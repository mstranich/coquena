---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.450198500Z
updated_at: 2026-10-21T23:16:24.450198500Z
---

# Unificar tamaños y memoria con escala 1024 y presentación legible

## Objetivo

Aplicar la convención funcional de unidades a la entrada y a todas las salidas humanas existentes, conservando exactitud interna.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [glosario.md#unidades-de-tamaño-y-memoria](../../docs/glosario.md#unidades-de-tamaño-y-memoria)
- [comandos.md](../../docs/comandos.md)
- [observabilidad.md](../../docs/observabilidad.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [ ] Centralizar parsing y formato con B/KiB/MiB/GiB/TiB/PiB/EiB y escala 1024; probar límites y valores inválidos conforme al contrato único.
- [ ] Rechazar los sufijos KB/MB/GB/TB/PB/EB; no conservarlos como aliases de entrada. Ajustar la ayuda a los únicos sufijos admitidos.
- [ ] Elegir magnitud automáticamente, limitar a dos decimales y quitar ceros innecesarios; comprobar promoción de magnitud tras redondeo.
- [ ] Sustituir presentaciones dobles y contadores extensos en bytes de listados, progreso, memoria, espacio, retención y diagnósticos propios de Coquena.
- [ ] Conservar cantidades exactas persistidas/estructuradas; no cambiar límites efectivos al cambiar etiquetas ni reinterpretar cifras formateadas por proveedores.
- [ ] Probar los puntos de salida existentes y exponer el formateador reutilizable para las entregas de observación y TUI.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Sin rediseñar telemetría, modificar valores numéricos persistidos o reabrir las evidencias históricas de M1/M2. No incorporar otra convención de presentación.

## Estado y evidencia

Planificada. Sin implementación ni validación ejecutadas. El inicio del trabajo, el cierre y cualquier commit requieren la autorización correspondiente.
