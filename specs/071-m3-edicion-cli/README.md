---
status: archived
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 046-m3-argumentos-schema
- 048-m3-backends
- 049-m3-modelos-companions
created_at: 2026-10-21T23:26:07.173365300Z
updated_at: 2026-11-04T19:52:46.481688800Z
completed_at: 2026-11-03T22:05:52.135984400Z
transitions:
- status: in-progress
  at: 2026-11-03T21:16:07.183989200Z
- status: complete
  at: 2026-11-03T22:05:52.135984400Z
- status: archived
  at: 2026-11-04T19:52:46.481688800Z
---
# Persistir cambios puntuales de entidades en el YAML seleccionado

## Objetivo

Integrar los comandos de configuración con intención YAML sin construir un editor interactivo ni convertir guardar en aplicar.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/design/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/design/definiciones-declarativas.md)
- [comandos.md](../../docs/design/comandos.md)
- [catalogo-de-parametros.md](../../docs/design/catalogo-de-parametros.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Decisiones de alcance

- Las definiciones de argumentos se declaran en YAML bajo `arg-defs`. Retirar la lectura de definiciones desde archivos JSON y actualizar los consumidores, la documentación funcional y la evidencia afectada.
- Los comandos de edición de definiciones escriben en la última capa seleccionada que declare `arg-defs`. Si ninguna la declara, crean la sección en la última capa seleccionada.
- `param reset` restablece únicamente el catálogo global.

## Trabajo y aceptación

- [x] Persistir set/bind/unset/clone y definición de entidades en archivo predeterminado o última capa seleccionada, validando antes/después.
- [x] Preservar comentarios/texto ajenos a la asignación y rechazar edición ambigua o conflicto de bytes/revisión; no reserializar destructivamente toda la receta.
- [x] Distinguir literal, referencia tipada, null que retira herencia y clon independiente sin copiar archivos.
- [x] Mantener name único raíz, service singular y presets independientes al editar; cambio de intención no renombra registros ni publica automáticamente.
- [x] Adaptar param, schema y restablecimiento con impacto visible, sin borrar asignaciones para esconder incompatibilidades.
- [x] Probar archivos manuales con overrides, edición externa concurrente y ausencia de descargas, refresh o reinicio por guardar.

## Dependencias

- [046: Validar argumentos y emitir CLI e INI con schema optativo](../046-m3-argumentos-schema/README.md)
- [048: Resolver backends de servicios globales y propios](../048-m3-backends/README.md)
- [049: Resolver inventario y adquisición de modelos y companions por ámbito](../049-m3-modelos-companions/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No bloquea lectura/emisión/preparación de recetas manuales. Sin init, editor de texto/formularios, borradores visuales o compatibilidad/migración de M2.

## Estado y evidencia

Implementación y validación terminadas.

- La CLI edita asignaciones puntuales de presets, backends, modelos, companions y
  definiciones de argumentos. Conserva la codificación y los bytes ajenos a la
  asignación. Los conflictos de capas se diagnostican antes del reemplazo.
- El catálogo declarativo efectivo se compone de `arg-defs.definitions` en YAML.
  Importación, preparación, publicación y actualización usan las capas YAML y env
  seleccionadas. El restablecimiento global conserva las asignaciones.
- `dotnet build tests/Coquena.Tests --no-restore` terminó sin advertencias.
  Con el ejecutable de pruebas se verificaron `--argument-tests` (167),
  `--recipe-tests` (323), `--recipe-import-tests` (24 de selección y 70 de integración),
  `--parameter-tests` (174), `--publication-tests` (43) y `--help-tests`.
- Se publicó con `dotnet publish src/Coquena -c Release -r win-x64` y se ejecutó
  el binario Native AOT con fixtures en D:. Se verificaron selección de capa,
  comentarios y UTF-16, literal y vínculo, clon, retiro de herencia, definición y
  retiro de parámetros, rechazo por incompatibilidad, edición de backend y
  recursos, restablecimiento global y ayuda. Guardar no creó home ni estado del
  proyecto. Una modificación externa tras preparar el candidato produjo
  `edit-conflict`, conservó los dos archivos y limpió el temporal.
- El ejemplo YAML del formato se leyó con `param list`. `git diff --check` pasó.

La edición rechaza reemplazos ambiguos, como atravesar un bloque retirado con
`null` o perder comentarios internos al sustituir un bloque completo. La comparación
de bytes se hace antes del reemplazo atómico. Los escritores externos al bloqueo
de Coquena pueden intervenir entre ambas operaciones.

Pruebas locales con fixtures. La aceptación integrada del milestone corresponde
a [074](../074-m3-aceptacion/README.md).
