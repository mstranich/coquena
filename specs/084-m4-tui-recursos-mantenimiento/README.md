---
status: complete
created: 2026-11-10
priority: medium
tags:
- tui
- dashboard
- m4
created_at: 2026-11-10T15:55:46.830108900Z
updated_at: 2026-11-10T17:19:27.642555700Z
completed_at: 2026-11-10T17:19:27.642555700Z
transitions:
- status: in-progress
  at: 2026-11-10T16:02:27.858561200Z
- status: complete
  at: 2026-11-10T17:19:27.642555700Z
---
# Completar inventario, adquisición y mantenimiento de recursos en la TUI

## Objetivo

Completar el recorrido de adquisición, inventario y mantenimiento de backends,
modelos y companions mediante O + W + S + C + R. Entrega de
[M4](../../docs/milestones/M4-experiencia-completa.md), sin autoría YAML ni Editor.

«Sin autoría YAML» significa no crear ni modificar declaraciones mediante estas
vistas. Consultar definiciones y referencias existentes sigue siendo necesario
para verificar recursos, conocer su uso y decidir qué se puede eliminar.

## Alcance acordado

| Vista | Entrega |
| --- | --- |
| O · Backends | Selección y detalle de instalación: variante/build, origen, ubicación, compatibilidad, protección y uso cuando exista evidencia. Activar protección/desprotección y eliminación con las restricciones del núcleo. Reutilizar K para descargar o registrar externos. |
| W · Inventario de modelos | Selección, filtro y detalle de modelos y companions; disponibilidad, origen, revisión, archivos/shards, tamaño, protección y referencias cuando exista fuente. Activar protección/desprotección, verificación y eliminación con las operaciones existentes. |
| S · Agregar modelo local | Registrar archivos externos en el inventario del ámbito elegido, con nombre, selección modelo/mmproj/mtp, tratamiento de shards y matching informativo opcional. Conservar propiedad externa y ubicación. |
| C · Hugging Face | Buscar repositorios/modelos GGUF, consultar información y README, seleccionar unidad, revisión, cuantización/archivos y companions explícitos, y descargar en el ámbito elegido. Volver al inventario y consultar la operación mediante N. |
| R · Mantenimiento | Verificar definición/recursos según ámbito y selección admitidos por la CLI; presentar resultados y diagnósticos. Previsualizar limpieza, recursos elegibles, protecciones y espacio recuperable, confirmar y mostrar el resultado real. |

Reutilizar K, N y los avisos existentes de la 083. Una descarga explícita prepara
inventario y no modifica YAML, publica presets ni inicia/reinicia servicios.
La verificación de W y R usa las mismas operaciones del núcleo con el alcance
correspondiente, sin construir un segundo motor de integridad o limpieza.

## Exclusiones explícitas

- Hallazgos 009, 010 y 011: no implementar cambio de backend de un servicio,
  unificación de Descargas ni investigación/corrección del caso de cancelación
  registrado durante la revisión de la 083. Permanecen fuera de esta entrega.
- No rediseñar N ni cambiar sus accesos por ámbito. Reutilizar su integración
  existente para las transferencias iniciadas desde C y K.
- En S, «declarar en receta» queda fuera: sólo alta de inventario. No habilitar
  escritura YAML mediante `model set`, formularios ni mecanismos alternativos.
- H e Y (Editor y conflictos de guardado), autoría de B/J/V, L (importación),
  gestión de presets P, parámetros Q y actualización U (`update`/`upgrade`).
- Comparadores de ejecuciones, pruebas de inferencia o asignación de modelos a
  presets, nuevas políticas de retención, calculadoras de recursos y Web UI.
- Paquetes nuevos, commits, push o cambios en la instalación global sin autorización.

La reutilización de N no acredita que los hallazgos de Descargas estén resueltos.
Si uno impide validar una integración de esta tarjeta, informar el bloqueo concreto
y consultar antes de absorber su solución o recortar el recorrido.

## Fuentes y base

- Diseño: [pantallas](../../docs/design/tui/README.md),
  [vista previa](../../docs/design/tui/vista-previa.html), wireframes
  [O](../../docs/design/tui/O-definiciones-backends.salt.puml),
  [W](../../docs/design/tui/W-definiciones-modelos-inventario.salt.puml),
  [S](../../docs/design/tui/S-modal-agregar-modelo-local.salt.puml),
  [C](../../docs/design/tui/C-definiciones-modelos.salt.puml) y
  [R](../../docs/design/tui/R-definiciones-mantenimiento.salt.puml).
- Reglas: [modelos y companions](../../docs/design/modelos-y-companions.md),
  [servidores](../../docs/design/servidores.md),
  [descargas](../../docs/design/descargas.md),
  [actualización y limpieza](../../docs/design/actualizacion-y-limpieza.md),
  [servicios y recetas](../../docs/design/servicios-recetas.md),
  [interfaces visuales](../../docs/design/interfaces-visuales.md),
  [comandos](../../docs/design/comandos.md) y
  [privacidad](../../docs/design/privacidad.md).
- Base entregada: 081 (lectura de Definiciones) y
  [083](../083-m4-tui-gestion-servicios/README.md) (gestión, K y N).
  Contratos existentes de adquisición, verificación y limpieza de M3, en particular
  048, 049 y 060. Son referencias de infraestructura disponible, no bloqueos pendientes.

## Diseño e integración por concretar

- [x] Revisar qué información del detalle tiene fuente real: uso y referencias,
      tamaño, compatibilidad, integridad e históricos de mantenimiento. No inferir
      éxito por desaparición de una operación ni inventar datos para igualar el boceto.
- [x] Concretar búsqueda remota, paginación, README y selección de unidades GGUF
      sobre los contratos disponibles. Reutilizar o extender las operaciones
      compartidas cuando falte una capacidad, sin un catálogo o ciclo exclusivo de TUI.
- [x] Revisar la verificación de instalaciones no referenciadas: `verify` valida
      definiciones; `--include-unused` pertenece al ámbito global. No asumir que
      existe el mismo selector para inventario de proyecto. Si el núcleo no permite
      una acción requerida por W/R, resolver el alcance con el usuario antes de
      implementar una política distinta o dejar un botón inoperante.
- [x] Contrastar selección por fila de limpieza y consulta de resultados anteriores
      con las capacidades reales de `clean` y `verify`. Mostrar lo respaldado por la
      fuente; consultar diferencias de comportamiento que la documentación no resuelva.

El tamaño o la colocación de un modelo no se deducen de una GPU disponible.
Una correspondencia remota no certifica procedencia ni integridad. No copiar ni
adoptar bytes externos por registrarlos o encontrar coincidencias.

## Plan

- [x] Completar O y su integración con K.
- [x] Completar W y el alta S, incluido el recorrido de shards y companions.
- [x] Implementar C y conectar descarga, inventario y seguimiento existente en N.
- [x] Implementar R y reutilizar su verificación desde W con selección explícita.
- [x] Validar recorridos, fallos, aislamiento de ámbitos y protecciones; registrar
      evidencia y reporte de hallazgos sin ampliar esta entrega.

## Criterios de aceptación

- [x] O, W, S, C y R mantienen la propuesta visual a 80×24 y aprovechan una terminal
      mayor. Recorrido completo con teclado, clic y foco visible en color.
- [x] Las acciones capturan ámbito e identidad de recurso/instalación antes de
      ejecutarse. Cambiar de página o proyecto no redirige operaciones en curso.
- [x] O y W muestran detalles verificables y distinguen disponibilidad, integridad,
      seguimiento remoto y propiedad. Los datos sin fuente no se inventan.
- [x] Protección/desprotección y eliminación conservan las reglas de la CLI;
      comprobar recursos activos, referenciados, protegidos, recuperación vigente,
      operaciones pendientes y archivos externos. Un error no se presenta como éxito.
- [x] S registra una unidad local simple, shards y companions; verificar conjunto
      incompleto, archivo inaccesible, nombre duplicado y matching ambiguo o fallido.
      Cancelar/fallar el matching conserva el registro según el contrato del núcleo.
- [x] C permite buscar, consultar información, seleccionar y descargar una unidad
      explícita; no descarga el repositorio entero ni companions por proximidad.
      Verificar errores remotos, ausencia de resultados y selección ambigua.
- [x] Una descarga desde C/K puede consultarse mediante N en el ámbito correcto.
      La navegación no cancela ni duplica la operación; las incidencias de los
      hallazgos excluidos se informan sin declararlas resueltas.
- [x] R distingue auditoría de definición y de recursos; muestra alcance y resultado,
      sin descargar, reparar, rebasar hashes ni cambiar la ejecución por verificar.
- [x] La limpieza muestra plan y espacio recuperable antes de confirmar. Cancelar
      conserva archivos; ejecutar revalida protecciones y muestra lo eliminado,
      lo bloqueado y los errores reales, sin exagerar bytes liberados.
- [x] Ningún recorrido crea o modifica YAML, locks de una nueva combinación ni
      presets, ni inicia/reinicia servicios. Los cambios de inventario, transferencias
      y observaciones locales son únicamente los propios de las acciones autorizadas.
- [x] Pruebas pertinentes y publicación Native AOT win-x64 con ejecución del binario:
      recorridos de alta local, búsqueda/descarga, inventario, verificación y limpieza
      sobre ámbitos aislados. La compilación administrada no sustituye esa evidencia.
- [x] Comparación visual con el usuario, evidencia anonimizada y límites explícitos;
      enlaces locales y `git diff --check` correctos. Validar sintaxis PlantUML y
      ejemplos JSON si se modifican esas fuentes.
- [x] Reporte de hallazgos en el chat, diferenciando hechos, restricciones y riesgos
      para M4 y Web UI. No implementar hallazgos ni crear registros adicionales
      sin autorización. Cerrar sólo con trabajo y evidencia completos.

## Estado y autorización

Implementación autorizada, completada y validada por el usuario.

Evidencia: O, W, S, C y R implementadas en `src/Coquena/DashboardResources.cs` y
`src/Coquena/DashboardCli.cs`, con búsqueda paginada en `HuggingFaceStore.SearchAsync`.
Las pruebas del panel pasan (160 comprobaciones); la publicación Native AOT win-x64
compila sin advertencias y el binario se ejecuta. El usuario recorrió la TUI y dio
por válida la implementación.

Ajustes durante la revisión con el usuario:

- C: una consulta en curso muestra una línea de estado con spinner braille y
  deshabilita sólo las acciones de red, en lugar de un modal.
- C: un único paginador `‹ ›` a la izquierda sobre una lista continua, de a 4
  resultados (las páginas del Hub se piden de a 20); `›` carga la página siguiente
  y el contador indica `de N+` mientras haya más.
- C: buscar y consultar repositorios es navegación; sólo los fallos generan avisos.
- El inventario de O y W se lee una vez por invalidación, no en cada redibujado.

Fuera de esta entrega siguen los hallazgos 009–011 de
[docs/findings](../../docs/findings/README.md).
