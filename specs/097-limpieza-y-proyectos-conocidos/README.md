---
status: archived
created: 2026-11-14
priority: medium
tags:
- cli
- tui
created_at: 2026-11-14T15:23:14.059868400Z
updated_at: 2026-11-18T03:11:37.770183500Z
completed_at: 2026-11-14T16:37:41.830795300Z
transitions:
- status: draft
  at: 2026-11-14T15:23:36.672005400Z
- status: planned
  at: 2026-11-14T15:45:20.185525100Z
- status: in-progress
  at: 2026-11-14T15:45:20.409803500Z
- status: complete
  at: 2026-11-14T16:37:41.830795300Z
- status: in-progress
  at: 2026-11-14T16:56:01.906384200Z
- status: complete
  at: 2026-11-14T16:56:54.911592Z
- status: in-progress
  at: 2026-11-14T18:55:59.801324400Z
- status: complete
  at: 2026-11-14T18:59:22.222673700Z
- status: in-progress
  at: 2026-11-14T19:27:39.264442Z
- status: complete
  at: 2026-11-14T19:35:38.843735Z
- status: in-progress
  at: 2026-11-15T00:28:46.949250900Z
- status: complete
  at: 2026-11-15T00:30:03.289036Z
- status: archived
  at: 2026-11-18T03:11:37.770183500Z
---
# Unificar la limpieza y permitir olvidar proyectos conocidos

## Objetivo y alcance autorizado

Corregir el mantenimiento de Coquena: consultar espacio recuperable y sus bloqueos,
limpiar recursos seleccionados por ámbito y depurar el índice de proyectos conocidos.
La CLI y la TUI consumen algoritmos comunes; cambia la interfaz de selección.
Sin milestone asignado.

La intervención comprende el núcleo y la CLI, y controles funcionales mínimos de
Mantenimiento en la TUI. El usuario reserva la revisión visual de la TUI para otro
agente. No incluye rediseño visual, paquetes nuevos, limpieza real de los recursos
del usuario ni commit.

## Reglas canónicas

- [Limpieza y protecciones](../../docs/design/actualizacion-y-limpieza.md).
- [Superficie CLI](../../docs/design/comandos.md).
- [Índice e identidad](../../docs/design/persistencia-proyectos.md).
- [Operaciones de descarga](../../docs/design/descargas.md).
- [Interfaces](../../docs/design/interfaces-visuales.md).

## Problemas comprobados

- La medición toleraba proyectos inaccesibles y anunciaba bytes libres mientras
  la limpieza global y local se bloqueaba por esas mismas referencias desconocidas.
- La CLI de usage conservaba opciones rechazadas y sus pruebas exigían
  --project-directory y rechazaban --global.
- No había filtros comunes de limpieza ni selección de proyectos conocidos por ID.
- El índice no permitía retirar todos los inaccesibles en una sola operación.
- Mantenimiento no estaba disponible en proyectos sin YAML.
- Usage capturaba cualquier ArgumentException de la operación como error de opciones.
  No se reprodujo el rechazo de usage sin argumentos en el checkout examinado;
  sí se verificó el diagnóstico capaz de ocultar la causa real.

## Decisiones de comportamiento

- Usage sin selección conserva el panorama de todos los ámbitos conocidos. Clean
  sin selección actúa sobre el proyecto actual. Los selectores explícitos son
  comunes; -f y --project son repetibles y se pueden unir con --global.
- -f sólo ubica la carpeta; el YAML puede no existir. --project recibe scope-id
  o prefijo único. Usage y clean rechazan --project-directory, --base, --env-file
  y --private. No se cambia la gramática de otros comandos.
- Los tipos se unen y los recursos puntuales restringen esa selección. Un tipo
  incompatible con un recurso se rechaza. Sin filtros se consideran instalaciones;
  --downloads sólo elige descargas, salvo al combinarse con tipos o recursos.
- Se decide por referencias registradas vigentes, no por hipótesis derivadas del
  índice. Un proyecto inaccesible sin relación no bloquea otros recursos. Las
  referencias obsoletas dejan de proteger; las referencias concretas cuyo destino
  no se puede leer conservan sólo sus recursos. Olvidar sigue siendo explícito.
- Project forget acepta uno o varios IDs, o --inaccessible para todos los inaccesibles.
  Hay vista previa y confirmación/--yes. La aplicación revalida el índice y los
  estados mostrados sin ampliar la selección. Sólo retira entradas del índice;
  puede cambiar la elegibilidad global, pero no borra archivos ni retira publicaciones
  o referencias independientes.
- Las descargas se limpian con el motor existente, bajo exclusión de ejecutor y
  comprobación del checkpoint y tamaño. Se conservan ejecutores activos y destinos
  físicos aún no incorporados al inventario. Retirar preparación terminal no borra
  instalaciones publicadas. Fallos mantienen evidencia recuperable y diagnóstico.
- Verify conserva su contrato: esta intervención no amplía su selección.
- TUI: «Olvidar» está en Mantenimiento de cualquier proyecto conocido, con o sin
  receta. Un proyecto que no se puede resolver no muestra otros nodos: su panel
  muestra el mensaje y, debajo, el botón «Olvidar». Global no ofrece «Olvidar».
  Como el árbol muestra un nodo por carpeta, olvidar desde la TUI retira todas las
  entradas del índice de esa carpeta, con vista previa y confirmación.

## Implementación

- CleanupSelection contiene filtros independientes de interfaz. ResourceCleanup
  evalúa medición y plan con el mismo recorrido y conserva la comprobación cruzada
  de ámbitos. Antes de borrar revalida referencias, propiedad y revisiones.
- CLI: usage/df y clean/prune comparten MaintenanceOptions y sus selectores.
  Los conjuntos de varios ámbitos se confirman juntos; al aplicar se releen las
  revisiones sin ampliar el conjunto confirmado. El borrado no es atómico entre
  ámbitos: un error posterior no revierte archivos ya retirados y se informa.
- ProjectIndex expone vista previa y aplicación del olvido individual o masivo.
- La TUI ofrece controles mínimos de modelos, auxiliares, backends y descargas.
  Cambiar filtros invalida la vista previa. Los proyectos accesibles sin receta
  ofrecen Mantenimiento; el ajuste visual sigue a cargo del agente de frontend.

## Verificación y evidencia

Validación de 2026-11-14, Windows win-x64; fixtures sintéticos, sin pesos remotos
ni servidores del usuario. No acredita rendimiento de eliminación de cientos de GiB.

- Compilación sin advertencias y publicación Native AOT completadas.
- Limpieza: 37 comprobaciones administradas y 55 con CLI Native AOT.
- Mantenimiento compartido y CLI: 36 comprobaciones, tanto en ejecución administrada
  como con el binario Native AOT para las invocaciones CLI. Incluyen selección por
  proyecto/ID y recurso/alias, ámbito global y varios ámbitos, YAML ausente,
  opciones retiradas, bloqueos por inaccesibles, olvido masivo con vista previa,
  revisión concurrente del índice, descargas activas, cambios entre vista previa y
  aplicación, limpieza parcial recuperable, filtros TUI y contexto sin receta.
- Transferencias: 57 comprobaciones aprobadas, con interrupción y recuperación
  entre procesos. Persistencia de proyectos también pasa, incluida recuperación
  entre procesos. Estas suites se ejecutan mediante su apphost; invocarlas con
  dotnet + DLL no permite relanzar correctamente sus workers y fue corregido en
  la ejecución, sin modificar las suites.
- Dashboard: se ejecutan Definiciones, fuentes y recursos; la suite se detiene en
  RecipeWriterTests con «missing intermediate maps are written in block», falla
  previa ya registrada. No corren sus comprobaciones posteriores.
- Ayuda general: la suite se detiene en una expectativa previa «download status»;
  el código anterior y actual anuncian «download status|show». No se cambió ese
  contrato ni se presenta esa suite como aprobada.
- Comprobaciones locales de sólo consulta con Native AOT: usage --global y
  project forget --inaccessible --dry-run funcionan. Clean --dry-run identifica
  el bloqueo por entradas inaccesibles; no se retiraron entradas ni recursos del
  usuario. Los fixtures acreditan que el olvido explícito desbloquea global/local.
- Enlaces locales comprobados; git diff --check con cr-at-eol para conservar el
  CRLF original de CliHelp.cs. No se modificaron diagramas PlantUML ni ejemplos JSON.
- LeanSpec board/search/view revisados. deps no está migrado en el adaptador;
  no hay dependencias declaradas y el alcance se revisó en el cuerpo.

## Hallazgos para revisión

- La política vigente distingue navegación de evidencia de uso. Una entrada
  inaccesible del índice no bloquea; una referencia registrada sólo conserva sus
  recursos mientras esté vigente. Las referencias con errores de lectura se
  conservan; las publicaciones a destinos ausentes o servicios retirados pueden
  dejar de proteger, sin desactivar las protecciones independientes del runtime.
- Una selección de varios ámbitos se confirma como conjunto pero se ejecuta por
  ámbito y las descargas por operación. Puede existir retirada parcial si cambia
  un participante o falla un borrado; se conservan diagnósticos, se informan bytes
  realmente liberados y no se amplía el conjunto confirmado. No hay rollback de
  archivos eliminados ni garantía de atomicidad de borrado entre ámbitos.
- El descubrimiento de referencias continúa leyendo los ámbitos relacionados,
  incluso con filtros pequeños. Es necesario para la seguridad; no se acreditó
  su coste a gran escala. Las futuras operaciones de Chat de M5 siguen sujetas
  a las mismas referencias de servicios activos; no se anticiparon funciones de
  ese milestone. Los textos nuevos siguen el idioma actual; i18n conserva su tarea.

Intervención implementada y lista para revisión de código, sin commit. Controles
TUI comprobados en presentación/selección; no se hizo un recorrido interactivo
ni revisión visual completa, que el usuario delegó para otra intervención.

## Corrección posterior: inicialización desde la TUI

Se confirmó una regresión introducida al dar contexto de mantenimiento a carpetas
sin YAML: DefinitionAction aún exigía Context == null para ejecutar Inicializar.
El botón se mostraba pero la acción no se ejecutaba. No afectaba a la CLI init.

La acción usa ahora la carpeta y una operación comprobable que verifica acceso,
ámbito de proyecto y ausencia de receta, independientemente del contexto de
mantenimiento. Un clic obsoleto después de aparecer el YAML se rechaza sin tocarlo.
Las pruebas ejecutan la inicialización con contexto no nulo, comprueban receta y
schema sin fabricar estado de ejecución, refrescan las vistas y verifican el
rechazo de una receta ya existente. Son 39 comprobaciones de mantenimiento, además
de las 37 comprobaciones administradas de limpieza. La cobertura anterior comprobaba
el contexto para mantenimiento pero omitía el recorrido Inicializar; ese hueco se
corrigió. No se realizó un recorrido gráfico interactivo.

Publicación Native AOT win-x64 de la corrección completada; ejecución posterior:
55 comprobaciones de limpieza y 39 de mantenimiento/CLI aprobadas. Sin commit.

## Corrección de redacción y preparación para i18n

A pedido del usuario, los textos de mantenimiento incorporados en esta intervención
se centralizan en Messages: consulta de espacio, listado y olvido de proyectos,
selección de limpieza, nuevos diagnósticos del núcleo y controles mínimos de la TUI.
Se conservan los códigos y datos persistidos; no se introduce un mecanismo de
idiomas ni se da por implementada la tarjeta 096. La ayuda general y el resto de
las interfaces conservan el alcance de esa tarjeta.

Se reemplaza la redacción técnica por el problema concreto y la acción disponible:
por ejemplo, explicar que Coquena no pudo leer algunos proyectos y no sabe qué
archivos siguen usando, en lugar de «Limpieza bloqueada por proyectos conocidos
sin comprobar». Las etiquetas de recursos de Mantenimiento y descargas también
usan Messages; las pruebas siguen comprobando códigos, datos y resultados.
No cambian las reglas funcionales ni las protecciones.

Validación de la redacción: compilación sin advertencias; 37 + 39 comprobaciones
administradas y 55 + 39 con CLI Native AOT aprobadas. Publicación renovada y salida
de usage revisada. git diff --check con cr-at-eol aprobado. Sin commit.


## Corrección del criterio de vigencia autorizada por el usuario

El usuario aclaró que recuperar espacio tras probar, fallar, editar y descartar es
parte central del producto. La regla anterior de bloqueo general fue incorrecta;
las observaciones y evidencias anteriores que describen ese bloqueo son históricas
y no constituyen el contrato vigente. Se reemplaza por análisis por recurso,
compartido entre usage y clean. No se borran recursos reales del usuario.

El índice sólo localiza proyectos. Una entrada ilegible no prueba uso de ningún
recurso. Una referencia concreta conserva sus recursos mientras sea vigente; un
error de lectura no acredita su desaparición. Se excluyen de la evidencia las
publicaciones hacia destinos ausentes o servicios retirados en estado verificado,
y contribuciones de fuentes desaparecidas en servicios detenidos sin publicación
pendiente. Activo, recuperación y operaciones pendientes se evalúan por separado.
La aplicación revalida el conjunto y rechaza referencias que reviven después de
la vista previa. No se retiran publicaciones del router ni registros del índice
implícitamente al limpiar archivos.

La documentación funcional cambia esta política de manera expresa a pedido del
usuario; no es sólo una mejora de redacción. La salida identifica los recursos
eliminables y la procedencia de referencias que conservan otros, con Messages.


Validación final de la corrección de vigencia (Windows win-x64): 37 comprobaciones
administradas de limpieza y 46 de mantenimiento/CLI; con el binario Native AOT
publicado, 55 de limpieza y las mismas 46 de mantenimiento/CLI aprobadas. Las
58 comprobaciones de publicación también pasan. Se comprobó con sólo consulta
que usage y clean --dry-run coinciden para un proyecto real con 116,16 GiB
recuperables y 11 recursos, aun con entradas inaccesibles en el índice. No se
borraron archivos reales ni entradas del índice. Enlaces locales y diff --check
con cr-at-eol aprobados; no hubo cambios en diagramas ni ejemplos JSON.

Para revisión: la desaparición comprobada de un destino es distinta de un error
de lectura. La detección usa estado publicado y comprobaciones de existencia,
no elimina registros ni modifica routers para declarar una referencia obsoleta.
Activos y recuperación pueden mantener una protección independiente. La
reaparición de destinos o cambios de revisión exigen repetir el análisis antes
de borrar. La intervención no mide rendimiento al eliminar cientos de GiB ni
acredita una revisión gráfica completa de la TUI. Sin commit.

## Compactación de usage por UX

A pedido del usuario, los eliminables y los conservados se muestran en secciones
separadas. Cada instalación conservada ocupa una sola línea con sus motivos
agrupados, sin repetir nombre ni ruta del propio ámbito para cada referencia.
Las instalaciones distintas con el mismo nombre se distinguen por ID corto cuando
coexisten en el reporte, para evitar mostrar el mismo nombre como borrable y
conservado sin explicación. Las rutas inaccesibles se agrupan con un único aviso
y comandos comunes, sin repetir un comando por entrada del índice. Toda redacción
nueva está en Messages. No cambian selección, elegibilidad ni borrado.
Validación de compactación: salida real revisada sin borrar archivos; compilación
sin advertencias; 37 + 46 comprobaciones administradas y 55 + 46 con CLI Native AOT
aprobadas. Publicación renovada. diff --check con cr-at-eol aprobado. Sin commit.
