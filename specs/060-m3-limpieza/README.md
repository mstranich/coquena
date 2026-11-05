---
status: archived
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 055-m3-publicacion-presets
- 059-m3-actualizacion
created_at: 2026-10-21T23:26:05.923210500Z
updated_at: 2026-11-04T19:52:46.474158400Z
completed_at: 2026-11-02T13:41:54.943000500Z
transitions:
- status: in-progress
  at: 2026-11-02T13:13:29.541403900Z
- status: complete
  at: 2026-11-02T13:41:54.943000500Z
- status: archived
  at: 2026-11-04T19:52:46.474158400Z
---
# Liberar recursos sólo con propiedad y referencias comprobadas

## Objetivo

Entregar limpieza explícita y gestión de protecciones sin borrar recursos ajenos o referenciados.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md#limpieza](../../docs/design/actualizacion-y-limpieza.md#limpieza)
- [persistencia-proyectos.md](../../docs/design/persistencia-proyectos.md)
- [comandos.md](../../docs/design/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar promoción a explícito y desprotección sin transferencia/borrado; conservar pins, activo, identidad incierta, última buena, combinaciones y operaciones.
- [x] Calcular elegibles y espacio con referencias cruzadas globales/proyectos bajo coordinación; raíz inaccesible no autoriza inferir ausencia de uso.
- [x] Implementar clean con preview, confirmación y --dry-run/--yes según gramática; revalidar revisión antes de eliminar y mostrar espacio legible realmente liberado.
- [x] Implementar listado/olvido de combinaciones conforme al contrato, sin borrar YAML/pesos, publicaciones activas o referencias independientes.
- [x] Retirar la retención automática de builds como política de limpieza; una edición o upgrade no autoriza borrar por inferencia.
- [x] Probar archivo externo interior, vecinos añadidos, eliminación parcial/reintento y contribuciones retiradas de un servicio compartido.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)
- [059: Aplicar planes selectivos con publicación conjunta recuperable](../059-m3-actualizacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Hallazgos relacionados

- [002: Coordinación entre monitor de descargas y limpieza](../../findings/002-coordinacion-monitor-limpieza.md).
  La retirada del área temporal puede causar FileNotFoundException desde el monitor
  y ocultar una finalización exitosa. Es distinta del clean de instalaciones de esta
  tarjeta; la referencia facilita encontrar la evaluación, sin incorporar su
  corrección al alcance ni añadir una dependencia.

## Límites

Sin deduplicación, barrido de YAML desconocidos o cascadas de archivos ajenos.

## Estado y evidencia

Implementación realizada y revisada con autorización del usuario. La tarjeta queda
`complete`; se creó un único commit, sin push, tag ni release.

- `CleanupCli`, `ResourceCleanup` y `CombinationCleanup` implementan la superficie
  nueva; se reutiliza protect/unprotect del inventario por ámbito. La protección
  explícita no cambia la propiedad ni elimina referencias independientes.
- Preview consulta las generaciones de las raíces conocidas, referencias de
  publicación y checkpoints. La ejecución vuelve a comprobar revisiones,
  elegibilidad, tamaño e identidad física bajo los bloqueos de escritores.
  Una ubicación inaccesible o identidad no comprobable conserva los recursos.
- Sólo se eliminan archivos enumerados como propios con identidad de volumen y
  archivo coincidente. Se conservan externos interiores, identidades externas
  compartidas, vecinos y directorios; la retirada parcial conserva inventario y
  diagnóstico, y el reintento cuenta sólo los bytes eliminados en ese intento.
- Forget retira entradas de los índices, resoluciones y última buena asociada en
  una generación recuperable. La retirada de proyecciones se reconcilia tras la
  decisión durable; activo, publicaciones y reservas independientes permanecen.
- Se retiran el ejecutor de retención de builds, sus llamadas desde instalación/
  upgrade y la opción `config backend-retention` de la CLI y ayuda. Los campos
  históricos de configuración no autorizan borrar y no se migra su persistencia.
- No se cambia documentación funcional: las reglas existentes resolvieron el
  comportamiento. Se amplía el mapa de código con el recorrido de limpieza.

### Validación ejecutada

Entorno y ubicaciones de prueba anonimizados; recursos locales y fixtures, sin
descargas de modelos ni servicios remotos. Las rutas reales quedan fuera de Git.

- Limpieza: 30 comprobaciones, incluidas ejecuciones del producto Native AOT.
  Cubren preview, rechazo de confirmación obsoleta, protección y desprotección,
  operaciones pendientes/canceladas, externos interiores, reemplazo de identidad,
  vecino añadido, eliminación parcial y reintento, revisión física exacta,
  forget con activo conservado, última buena retirada y raíz inaccesible.
- Dos consumidores de un servicio compartido detenido: publicación cruzada,
  retirada confirmada y limpieza conservando la contribución ajena. Son fixtures
  del núcleo de publicación, no evidencia de inferencia o router real.
- Regresiones: persistencia y recuperación entre procesos correctas; publicación
  41, combinaciones 69, upgrade de recetas 29 y upgrade de backends 26
  comprobaciones. La regresión de backends verifica ocho builds conservados aun
  con una preferencia histórica de retención igual a uno.
- Inventario y protección existentes: modelos por ámbito 53 y backends por ámbito
  56 comprobaciones, incluidas sus ejecuciones de CLI Native AOT.
- Publicación Native AOT `win-x64` correcta, con estado y recursos de las nuevas
  pruebas en el volumen acordado. El producto publicado ejecuta dry-run, rechazo
  sin confirmación, limpieza con yes, rechazo de force y list/forget.
- Ayuda correcta en administrado y Native AOT, incluidos 16 errores de gramática
  sin efectos. Build sin errores ni advertencias, siete enlaces locales y 237
  referencias del mapa existentes; `git diff --check` y revisión de privacidad
  correctos. No se modifican PlantUML ni ejemplos JSON de documentación.
- Board/search/view revisados; dependencias 055 y 059 completas. Los comandos
  `leanspec deps` y `leanspec tokens` no están migrados al adaptador disponible;
  las dependencias se comprobaron mediante las tarjetas y el board.

Reproducción con un directorio nuevo y vacío por recorrido:

```powershell
$TestRoot = Join-Path 'D:/tmp' ('coquena-cleanup-' + [guid]::NewGuid())
$Published = Join-Path $TestRoot 'published'
dotnet publish src/Coquena -c Release -r win-x64 -o $Published
dotnet run --project tests/Coquena.Tests -- --cleanup-tests "$TestRoot/cleanup" "$Published/coquena.exe"
```

No se ejecutó la suite general ni la aceptación integrada de M3. Los snapshots
activos de limpieza son fixtures; no se prueba limpieza durante inferencia real
ni rendimiento con inventarios grandes. No se modifica el monitor de descargas
del hallazgo 002.

### Aspectos para revisión

Se consulta y coordina el conjunto de proyectos conocidos y destinos encontrados.
Una raíz conocida inaccesible impide acreditar la limpieza del conjunto; no se
supone que no usa recursos. La publicación compone generaciones de todos los
participantes comprobados. Esto conserva la serialización con nuevas referencias,
pero su costo crece con el número de ámbitos y documentos, y no se midió a gran
escala. Evaluar esa restricción para catálogos grandes y las interfaces de M4,
sin anticipar otra política de propiedad ni crear tareas adicionales.

La retirada de archivos y el commit de inventario no forman una transacción del
sistema de archivos: una interrupción puede conservar el inventario anterior con
archivos propios ya ausentes. El reintento los reconoce como ausentes y no vuelve
a contarlos como liberados; los errores observados conservan diagnósticos. No se
promete revertir un archivo eliminado si falla la publicación de la generación.
