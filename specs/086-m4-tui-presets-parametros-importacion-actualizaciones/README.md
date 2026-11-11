---
status: complete
created: 2026-11-10
priority: medium
tags:
- tui
- dashboard
- m4
created_at: 2026-11-10T21:56:14.203893300Z
updated_at: 2026-11-11T01:58:06.567765500Z
completed_at: 2026-11-11T01:58:06.567765500Z
transitions:
- status: in-progress
  at: 2026-11-10T21:59:36.386454400Z
- status: complete
  at: 2026-11-11T01:58:06.567765500Z
---
# Completar Presets, Parámetros y Actualizaciones e incorporar Importar y Nueva combinación

## Objetivo

Completar P, Q y U e implementar L y V en la TUI, integrando las cinco vistas
pendientes de la propuesta. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).
La entrega completa estos recorridos; no implica cerrar automáticamente el milestone.

## Alcance

| Vista | Entrega |
| --- | --- |
| P · Presets | Selección y detalle, modelo/companions con acceso al inventario, procedencia del YAML y comparación entre receta y ejecución cuando exista evidencia. Activar Clonar, Editar YAML, Eliminar y Ver en vivo, reutilizando H/Y y las operaciones compartidas. |
| Q · Parámetros | Catálogo efectivo del ámbito/combinación, filtro y detalle: tipo, aliases, descripción, opciones/default, ámbito, destino, aplicabilidad y origen disponibles. Activar Definir, Editar y Quitar definición con validación e impacto sobre asignaciones existentes. |
| U · Actualizaciones | Elegir combinación y recursos admitidos por el núcleo. Comprobar consulta y guarda el plan (`update`); Actualizar aplica las propuestas válidas seleccionadas del plan persistido (`upgrade`). Mostrar vigencia, pins, bloqueos, errores y efecto sobre la ejecución sin reiniciarla. Reutilizar N para las transferencias. |
| L · Importar en Global | Selección explícita de categorías y elementos desde la combinación elegida; mostrar colisiones y conservar/reemplazar según el contrato de importación. Ofrecer servicio predeterminado cuando corresponda. Mostrar preparación y resultado real, con las confirmaciones exigidas para publicación o sustitución. |
| V · Nueva combinación | Elegir archivos YAML y su orden, seleccionar archivos `.env`, agregar archivos nuevos mediante H y aceptar/cancelar la selección. Integrar el acceso desde B y los accesos de agregar override que correspondan a este recorrido. La combinación se registra al usarla en Up o Iniciar, no por aceptar el modal. |

Reutilizar el Editor y los conflictos de guardado existentes; distinguir definición,
borrador, selección, plan y composición activa. Las acciones conservan el ámbito y
las identidades capturadas, aunque el usuario navegue durante su ejecución.

## Fuentes y base entregada

- Diseño: [pantallas](../../docs/design/tui/README.md),
  [vista previa](../../docs/design/tui/vista-previa.html) y wireframes
  [P](../../docs/design/tui/P-definiciones-presets.salt.puml),
  [Q](../../docs/design/tui/Q-definiciones-parametros.salt.puml),
  [U](../../docs/design/tui/U-definiciones-actualizaciones.salt.puml),
  [L](../../docs/design/tui/L-importar.salt.puml) y
  [V](../../docs/design/tui/V-modal-nueva-combinacion.salt.puml).
- Reglas canónicas: [interfaces visuales](../../docs/design/interfaces-visuales.md),
  [formato de proyecto](../../docs/design/formato-proyecto.md),
  [definiciones declarativas](../../docs/design/definiciones-declarativas.md),
  [parámetros](../../docs/design/catalogo-de-parametros.md),
  [servicios y recetas](../../docs/design/servicios-recetas.md),
  [actualización](../../docs/design/actualizacion-y-limpieza.md),
  [comandos](../../docs/design/comandos.md) y
  [privacidad](../../docs/design/privacidad.md).
- Base entregada: 081 (lectura P/Q/U), 083 (gestión, publicación y N),
  [084](../084-m4-tui-recursos-mantenimiento/README.md) (inventario/adquisición) y
  [085](../085-m4-tui-recetas-editor/README.md) (B/J/H/Y).
  Son referencias a trabajo terminado, sin nuevos bloqueos de implementación.

## Diseño e integración por concretar

- [x] Revisar el código actual mediante `src/code-map.yaml` y las operaciones
      compartidas antes de implementar. Contrastar cada control del boceto con
      su contrato, incluyendo selección por recurso en U y categorías de L.
- [x] Resolver la procedencia y el destino de escritura de P/Q con capas YAML,
      overrides y ámbito Global. No sustituir el catálogo efectivo del proyecto
      por el catálogo global histórico ni modificar asignaciones silenciosamente.
- [x] Concretar Clonar/Eliminar y los cambios estructurados de Q sobre borradores
      y archivos abiertos: conservar cambios, validación y protección frente a
      cambios externos. Usar H donde corresponda sin construir otro Editor.
- [x] Comprobar fuentes de comparación receta/en vivo, uso de parámetros y vigencia
      del plan. Sin evidencia, mostrar ausencia o diagnóstico; no inventar valores,
      estados ni atribuciones para reproducir los datos ilustrativos.
- [x] Contrastar V con composición, privacidad y registro existentes, incluidos
      base, orden, duplicados, `.env` y archivos nuevos todavía no guardados.
- [x] Si un comportamiento del diseño no está resuelto por las reglas y el núcleo,
      consultar y detener sólo esa parte antes de cambiar políticas o reducir alcance.

## Plan

- [x] Implementar V e integrar selección/archivos nuevos con B y H/Y.
- [x] Completar P con detalle, comparación y acciones sobre presets.
- [x] Completar Q sobre el catálogo efectivo y las operaciones de edición compartidas.
- [x] Implementar L con selección, colisiones, preparación y resultado de importación.
- [x] Completar U con consulta, plan persistido, selección y aplicación recuperable.
- [x] Validar recorridos y diseño con evidencia anonimizada y reporte de revisión.

## Criterios de aceptación

- [x] Fidelidad a la propuesta a 80×24 y en terminal mayor: jerarquía, tabla/detalle,
      paleta, zonas sin bordes, desplazamiento, barras persistentes y foco visible.
      Todos los controles funcionan con teclado y clic. Comparación visual con el usuario.
- [x] P permite recorrer preset → modelo/companion → inventario, editar/clonar en
      H y volver. Eliminar definición muestra su efecto; no borra pesos ni retira
      publicaciones por inferencia. Ver en vivo selecciona la entrada correcta.
- [x] P separa valores guardados, borradores y observados, indicando datos ausentes
      o ejecución distinta. Guardar no publica ni reinicia.
- [x] Q valida definiciones y asignaciones afectadas con el núcleo: tipos, aliases,
      defaults, ámbitos, restricciones y modos estricto/permisivo. Error o cancelación
      conserva el estado anterior; quitar schema no elimina valores ni recursos.
- [x] V preserva el orden y selección explícitos de YAML/`.env`; aceptar no ejecuta
      ni registra una combinación usada. Crear archivo requiere guardado explícito;
      cancelar no escribe ni descarta borradores silenciosamente. Valores privados
      no aparecen en avisos ni evidencia pública.
- [x] L permite selección parcial y diagnostica colisiones antes de sustituir.
      Mantiene propiedad externa, aislamiento de ámbitos y referencias; no inicia
      ni publica fuera de las acciones/confirmaciones del contrato de importación.
      Fallos o cancelación muestran el resultado realmente persistido.
- [x] U comprueba sólo la combinación y filtros elegidos; pins no se convierten en
      latest. Error/ambigüedad no equivale a ausencia de novedades. Un plan invalidado
      exige Comprobar de nuevo, sin recalcular otra selección durante Actualizar.
- [x] U conserva recursos bloqueados y aplica las propuestas válidas como conjunto:
      todas se preparan/verifican antes de publicar sus resoluciones conjuntamente.
      Fallos conservan la resolución anterior; reintento/reanudación usa la selección
      exacta. No modifica YAML ni archivos en uso, ni reinicia servicios.
- [x] Navegación y operaciones en segundo plano conservan ámbito/identidad, no
      duplican trabajos ni bloquean la interfaz. Resultados fallidos no muestran éxito.
- [x] Pruebas pertinentes de edición, composición, parámetros, importación y planes,
      con casos negativos y aislamiento; publicación Native AOT win-x64 y ejecución
      del binario en los cinco recorridos sobre ámbitos de prueba.
- [x] Evidencia y límites reales en esta tarjeta, revisión visual y reporte de
      hallazgos en el chat. Enlaces locales y `git diff --check` correctos; validar
      PlantUML/JSON si se modifican esas fuentes. Completar sólo con trabajo y evidencia.

## Exclusiones y autorización

- Los hallazgos previamente excluidos, incluidos 009–011, siguen fuera: no cambio
  de backend de servicio, unificación de Descargas ni corrección del caso de cancelación.
  Reutilizar N no acredita resolverlos; consultar si bloquean un recorrido autorizado.
- Sin benchmarking, calculadoras, comparadores de experimentos, extracción automática
  exhaustiva de schemas, nuevo Editor ni Web UI.
- No agregar paquetes, hacer commits/push ni cambiar la instalación global sin autorización.
- Implementación autorizada por el usuario. Permanece `in-progress` hasta la revisión
  visual y la reconciliación final de los criterios de aceptación.

## Decisiones confirmadas e integración

- L selecciona categorías completas, como la CLI. La lista de entidades permite
  autorizar reemplazos concretos; conservar una colisión incompatible detiene la
  importación, sin omitirla silenciosamente. El usuario confirmó esta selección.
- P/Q editan intención global sin aplicarla. El usuario confirmó incorporar una
  operación compartida para publicar la generación, en vez de escribir directamente
  las proyecciones. Q local usa el schema de la combinación, sin sustituirlo por el
  catálogo global histórico.
- Clonar, quitar preset y definir/quitar schema generan borradores en H. Q permite
  completar todos los metadatos en YAML y rechaza guardar un schema incompatible.
  El Editor global conserva Guardar/Validar; Up/Down locales no se ofrecen sobre
  intención global. Guardar no publica ni inicia procesos.
- P compara la receta con el snapshot de INI registrado, rotulado «Publicado».
  El estado del router se muestra separado. No hay una lectura uniforme de todos
  los argumentos efectivos del backend; una casilla sin fuente muestra «—».
- V mantiene selección de sesión para B/P/Q/U. Los archivos nuevos permanecen
  como borradores hasta Guardar y la combinación no se registra por aceptar V.
  Conserva la preparación privada explícita; `.env` y las selecciones que el
  núcleo clasifica automáticamente como privadas mantienen esa clasificación.
- U lee el plan de la selección exacta, distingue vigente/inválido y permite
  marcar propuestas aplicables. Los trabajos capturan ámbito, combinación y filtros.

## Implementación y evidencia técnica

Archivos principales: `DashboardAuthoring.cs`, `DashboardCli.cs`, `DashboardEditor.cs`,
`Projects/ImportedRecipeEdits.cs` y `Recipes/RecipeEdit.cs`. `Propose` comparte la
edición puntual con la CLI sin escribir al preparar el borrador. El guardado global
comprueba proyecciones y revisión, valida las intenciones afectadas y conserva las
ediciones manuales ya presentes al abrir. Un cambio común valida todos sus consumidores.
La importación usa el mismo recorrido de aplicación que la CLI con interacción de TUI;
la consulta y aplicación de actualizaciones usan `RecipeUpdatePlans` y `RecipeUpgrade`.

Validación ejecutada en Windows, sobre ámbitos aislados y fixtures sintéticas:

| Comprobación | Resultado |
| --- | --- |
| Build de producto y pruebas | Sin advertencias ni errores |
| `--dashboard-tests` | 207 comprobaciones; incluye autoría, borradores nuevos, schema efectivo, conflictos y generaciones globales |
| `--recipe-import-tests` | 24 comprobaciones de selección y 70 de integración |
| `--recipe-update-tests` | 73 comprobaciones administradas; 78 incluyendo ejecución del producto Native AOT |
| `--recipe-upgrade-tests` | 29 comprobaciones administradas; 30 incluyendo recuperación y publicación de candidatas exactas mediante el producto Native AOT, sin red |
| Publicación win-x64 Native AOT | Sin advertencias; ejecutable ejecutado en terminal |
| Recorrido TUI a 80×24 | P: clonar y guardar; Q: definir/guardar local y global; V: seleccionar base + override y aceptar sin registrar; L: importar sólo parámetros; U: comprobar plan de combinación sin recursos |
| Terminal 120×36 y teclado | Navegación a U, Comprobar con Tab/Enter, aceptar filtro vacío por teclado y consulta de plan vigente; barras persistentes y contenido ampliado |
| V en el binario final | Selección privada explícita sobre una combinación ya registrada: B conserva la elección de sesión y `combination list` conserva el registro anterior, sin ejecutar ni registrar de nuevo |

En la prueba nativa de guardado global se comprobó el avance de generación y la
ausencia de documentos de ejecución activa. Las rutas reales de las pruebas y las
salidas de terminal permanecen en evidencia local excluida de Git; no se publica
el perfil de hardware ni se acredita inferencia con estas fixtures.

### Revisión de uso a 80×24

Un recorrido con clics sobre un ámbito de prueba aislado detectó tres recorridos que
sólo podían fallar y se corrigieron: U deshabilita Comprobar y explica que la
combinación debe usarse antes en Up o Iniciar; L avisa antes de confirmar que los
presets requieren un servicio global predeterminado; Clonar explica el nombre
inválido y reabre el formulario. Los diálogos de mensaje muestran ahora todas sus
líneas, Q recorta su detalle y U parte sus avisos al ancho del contenido.
`--dashboard-tests` suma 220 comprobaciones, incluidas pruebas de diseño bajo estrés;
la publicación Native AOT se ejecutó y los tres casos se comprobaron en el binario.
Las inconsistencias restantes quedan para evaluación en los hallazgos
[012](../../docs/findings/012-coherencia-visual-tui.md),
[013](../../docs/findings/013-estado-navegacion-tui.md) y
[014](../../docs/findings/014-procedencia-y-filtros-autoria.md).

### Límites de aceptación

El usuario revisó la TUI y dio la tarea por cerrada: las cinco vistas están cubiertas.
Las inconsistencias de uso que quedan se corrigen en entregas siguientes de cohesión
y consistencia, a partir de los hallazgos 012–014.
El recorrido nativo de U en TUI comprobó consulta y estado del plan sin recursos;
su aplicación recuperable se acreditó mediante el motor y el comando Native AOT.
No se acredita aquí inferencia real, comparación de P con un router de producción,
una descarga remota nueva mediante U ni rendimiento con catálogos globales grandes.
Los hallazgos 009–011 siguen excluidos. No se hicieron commits, push, instalación
global ni cambios de paquetes durante la implementación; el cierre se registró con commit autorizado.
