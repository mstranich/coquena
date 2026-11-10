---
status: complete
created: 2026-11-10
priority: medium
tags:
- tui
- dashboard
- m4
created_at: 2026-11-10T13:49:50.625498500Z
updated_at: 2026-11-10T15:43:58.108836Z
completed_at: 2026-11-10T15:43:58.108836Z
transitions:
- status: in-progress
  at: 2026-11-10T14:04:45.280779600Z
- status: complete
  at: 2026-11-10T15:43:58.108836Z
---
# Completar gestión de servicios, backends y descargas en la TUI

## Objetivo

Completar el recorrido visual de gestión de servicios y sus dependencias de
backends y descargas, sobre la capa de lectura existente. Entrega de
[M4](../../docs/milestones/M4-experiencia-completa.md), sin implementar el Editor.

## Alcance acordado

| Vista | Entrega |
| --- | --- |
| M · Servicios / Servicio | Selección de filas y detalle: backend, puerto, estado, archivo, argumentos y consumidores cuando exista fuente. Alta global, designación de predeterminado, eliminación con las protecciones existentes y navegación al servicio seleccionado en En vivo. Distinguir las operaciones disponibles en Global de las del servicio propio de un proyecto. |
| Alta de servicio | Diseñar e implementar el formulario que falta en la propuesta. Reutilizar el contrato de creación existente: crear no inicia el servicio ni cambia una instancia activa. |
| K · Seleccionar backend | Origen oficial o ejecutable externo, compatibilidad, versión y preparación explícita. Concretar el enlace con el alta sin equiparar descarga con asignación al servicio. |
| N · Descargas | Consulta y actualización de progreso/estado, pausa, reanudación y cancelación con las operaciones existentes. Conectar el indicador superior con datos reales y navegación a Descargas, conservando el ámbito. |
| A · En vivo | Reutilizar los controles y logs existentes; «Ver en vivo» enfoca el servicio elegido en M. |
| X · Confirmar Up | Modal de confirmación de publicación sobre un destino ocupado, con Cancelar y Publicar igual. Incluir el acceso a Up desde Recetas necesario para recorrerlo con una combinación existente, sin depender del Editor. |

El acceso a K desde Backends forma parte del recorrido. Esto no autoriza completar
todas las demás acciones de O ni otras secciones de Definiciones.

«Editar YAML» permanece deshabilitado. Quedan fuera H (Editor), Y (conflicto de
guardado), autoría de recetas/presets y Down. Se incluye Up para integrar X;
no implica completar las demás acciones de Recetas.
No se agregan paquetes ni se hacen commits sin autorización.

## Fuentes y base

- Propuesta: [pantallas](../../docs/design/tui/README.md),
  [vista previa](../../docs/design/tui/vista-previa.html), wireframes
  [M](../../docs/design/tui/M-definiciones-servicios.salt.puml),
  [K](../../docs/design/tui/K-seleccionar-backend.salt.puml) y
  [N](../../docs/design/tui/N-definiciones-descargas.salt.puml) y
  [X](../../docs/design/tui/X-modal-confirmar-up.salt.puml).
- Reglas: [servidores](../../docs/design/servidores.md),
  [servicios y recetas](../../docs/design/servicios-recetas.md),
  [descargas](../../docs/design/descargas.md),
  [interfaces visuales](../../docs/design/interfaces-visuales.md),
  [comandos](../../docs/design/comandos.md) y
  [privacidad](../../docs/design/privacidad.md).
- Base entregada: 080 (En vivo), 081 (Definiciones de lectura) y 082
  (Configuración). Contratos existentes: 048 (backends) y 053 (servicios).
  Estas referencias describen la base disponible, no bloqueos pendientes.

## Diseño por concretar antes de implementar el flujo afectado

La propuesta no desarrolla el formulario de «+ Agregar». El alta CLI crea un
servicio oficial sin YAML y prepara su backend al iniciar; K representa gestión
de backends y no define por sí sola su asignación al servicio.

- [x] Revisar los contratos y presentar el formulario de alta y su enlace con K:
      campos, persistencia, backend oficial/externo y preparación opcional.
      Resolver con el usuario cualquier decisión funcional que esos contratos
      no determinen antes de implementar esa parte.
- [x] Concretar selección de transferencias y navegación por ámbito desde el
      indicador global, sin perder operaciones al cambiar de proyecto.

Conservar los contratos de la CLI; no resolver estas cuestiones introduciendo
políticas implícitas. Los datos sin fuente no se inventan. Las reglas funcionales
sólo se modifican si se acuerda un cambio, explicándolo para revisión.

## Plan

- [x] Completar selección y detalle de M y sus acciones de gestión.
- [x] Implementar el alta y el recorrido K conforme al diseño acordado.
- [x] Activar las acciones de N y el indicador superior de descargas.
- [x] Conectar M con A, preservando identidad y ámbito del servicio.
- [x] Integrar Up de una combinación existente desde Recetas y X con las
      comprobaciones y aceptación explícita del riesgo de publicación de la CLI.
- [x] Validar el recorrido completo y registrar evidencia y hallazgos.

## Criterios de aceptación y validación

- [x] M, K, N, X y el alta mantienen la propuesta visual a 80×24 y aprovechan una
      terminal mayor; recorrido con teclado, clic y foco visible en color.
- [x] Alta global, default y eliminación usan las mismas operaciones y
      protecciones de la CLI; se prueban nombre duplicado, servicio activo,
      restricciones de consumidores y errores sin cambios parciales ocultos.
- [x] Crear no inicia; descargar no asigna ni reinicia implícitamente. Selección
      oficial justifica compatibilidad; archivos externos conservan su propiedad.
- [x] Preparación oficial y selección externa se verifican con fallos de
      compatibilidad o archivo ausente, sin sustituir silenciosamente el backend.
- [x] N e indicador reflejan operaciones reales; pausa, reanudación y cancelación
      se verifican durante una transferencia, conservada al cambiar de ámbito.
- [x] «Ver en vivo» abre el servicio correcto y permite usar sus controles y logs
      existentes. Cerrar la TUI conserva servidores desacoplados.
- [x] X muestra destino y riesgo con datos disponibles, sin inventar actividad.
      Cancelar no publica; Publicar igual autoriza la operación explícitamente.
      Verificar destino ocupado y libre, errores y resultado observado, sin
      convertir la confirmación de publicación en confirmación de inicio.
- [x] Pruebas pertinentes y publicación Native AOT win-x64 con ejecución del
      binario en los recorridos anteriores; no basta compilación administrada.
- [x] Comparación visual con el usuario y evidencia anonimizada, con límites
      explícitos; git diff --check y enlaces locales comprobados.
- [x] Reporte de hallazgos en el chat y evidencia en la tarjeta, distinguiendo
      hechos, decisiones acordadas y riesgos para la evolución de M4/Web UI.

## Estado y autorización

Implementación autorizada por el usuario. No se autorizan paquetes nuevos ni commits.

## Decisiones confirmadas durante la implementación

- El alta permite elegir backend oficial o externo y guarda esa configuración.
- Incluye host, puerto y, para externos, URL de salud explícita y argumentos de
  proceso mediante formulario. No requiere el Editor YAML.
- La preparación opcional no inicia el servicio. K desde Backends prepara recursos
  del ámbito sin asignarlos a un servicio ni editar una receta existente.

## Implementación y evidencia — 2026-11-10

- Gestión y formularios en `DashboardManagement.cs`; presentación en
  `Observation/ManagementPresentation.cs`. Selección de servicios por identidad,
  ámbito capturado antes de lanzar operaciones y controles de transferencia por GUID.
- El alta reutiliza `PublicationCli.EnsureGlobalAsync`, extendida con configuración
  explícita y validación previa a publicar la definición. Conserva la selección
  automática de la CLI cuando no se proporcionan esos campos.
- M distingue una definición local sin registrar de un servicio registrado. La
  consulta no prepara estado; permite pasar a En vivo para iniciar explícitamente.
- N usa las mismas lecturas y controles que la CLI; las continuaciones y preparaciones
  corren fuera del hilo de UI. El indicador consulta Global y los proyectos conocidos;
  al pulsarlo se elige ámbito. Los errores de lectura se notifican, sin inventar datos.
- Up conserva YAML en orden, archivos env y selección privada de la combinación
  registrada. X reutiliza la consulta de actividad y la confirmación de la CLI;
  el destino se transmite explícitamente a la interacción visual.
- Los mensajes modales usan texto desplazable para que sus advertencias cortas se
  dibujen. Los botones auxiliares del alta no aceptan ni cierran el formulario padre.
- Los resultados de Up distinguen éxito, cancelación y error en los avisos, con el
  ámbito de la operación capturado; no se atribuyen al servicio enfocado después.

### Validación ejecutada

- Suite del dashboard administrada y Native AOT: 152 comprobaciones, incluidas alta oficial y
  externa, default, nombres duplicados, ejecutable ausente, puerto/salud inválidos,
  preservación de archivos al eliminar, cancelación y definición sin registrar.
- El worker de transferencia de la TUI se prueba con transporte controlado:
  pausa durante ejecución, reanudación hasta verificación/publicación y cancelación
  en otro ámbito con el mismo GUID sin afectar la instalación terminada.
- Suites Native AOT: publicación (50), transferencias (57), servicios (37), backends
  por ámbito (49). Incluyen protecciones de ciclo/consumidores, incompatibilidad,
  integridad y recuperación. Fixtures sintéticos no acreditan inferencia del backend.
- Recorrido del producto Native AOT win-x64 mediante PTY: alta externa con host,
  puerto, salud y argumento de proceso, lectura cruzada con `service show`, enlace
  a En vivo e inicio contra un backend real. Crear no inició el proceso.
- K preparó un backend CPU oficial en el proyecto sin cambiar su YAML ni su instancia
  activa. N dejó de listar la operación después de su publicación y limpieza.
- Up de combinación registrada publicó una entrada sobre un router ocioso. Durante
  inferencias reales, X mostró la advertencia; Cancelar devolvió código 130 y conservó
  los argumentos publicados anteriores. Se detuvieron los servicios de prueba.
- La suite general ejecutó 14 casos de CLI administrados y 14 del binario publicado,
  y los contratos M1; luego se detuvo en `help fits 80 columns`. La ayuda no se modificó
  en esta entrega; la misma limitación está registrada en 080 y 082.
- PlantUML del alta comprobado con `-checkonly`, JavaScript de la vista previa válido,
  enlaces Markdown y rutas del mapa comprobados; `git diff --check` sin errores.
- Binario de revisión publicado en `artifacts/083/delivery/`; evidencias locales
  excluidas de Git en `artifacts/083/`. El artefacto final se ejecutó en PTY y se
  recorrió Definiciones; los servicios de prueba quedaron detenidos.

### Hallazgos y límites de aceptación

- **Histórico de descargas limitado por la fuente.** El motor retira las operaciones
  completadas tras limpiar. N muestra los registros que conserva el motor y los
  resultados quedan en avisos de sesión; no se inventa éxito por desaparición ni
  se agregó persistencia propia. Evaluar la expectativa de «Terminadas» del diseño
  frente a esa fuente; afecta también a una futura Web UI.
- **Backend externo no implica compatibilidad acreditada.** Se guarda su ubicación
  y configuración explícita, sin copiar archivos ni inferir flags adicionales.
  La salud y el inicio real se verifican cuando se ejecuta el servicio, según el
  contrato existente. La variante oficial se resuelve y comprueba con el núcleo.
- **Costo de consulta del índice.** El indicador recorre los ámbitos conocidos en
  cada intervalo; el costo crece con el número de proyectos. Se probó aislamiento
  entre ámbitos, no un índice grande ni rutas lentas. La posibilidad de demoras en
  esos casos es un riesgo sin medición, a evaluar para la evolución de M4/Web UI.
- Los recorridos PTY no acreditan una comparación visual con el usuario a 80×24,
  truecolor y terminal mayor. Sigue pendiente esa revisión conjunta, incluido el
  flujo de aceptación de «Publicar igual» en X desde la UI y todas las acciones de
  N por teclado/clic; las operaciones subyacentes sí tienen pruebas automatizadas.
- La documentación funcional incorpora la decisión de alta configurable aprobada
  por el usuario; no se cambiaron reglas para justificar una implementación distinta.
  El nuevo boceto T y la vista previa representan ese formulario. K en el contexto
  de inventario se limita a preparar, sin habilitar autoría de recetas existentes.
- Sin paquetes nuevos, commit, push ni actualización de la instalación global.
  Revisión visual y recorrido validados por el usuario el 2026-11-10.

### Revisión de consistencia visual — 2026-11-10

- M: encabezado y filas comparten columnas (servicio 15, backend 17, puerto 8);
  la fila elegida se resalta completa y el detalle alinea sus rótulos como la
  vista previa («· predeterminado», rótulos en columna 3).
- N: los controles se dibujan en la fila de estado de la vista previa, sin
  perder «❚❚ pausada»; «Reintentar» queda en la fila terminada que falló.
  `DefinitionsPresentation.Downloads` habilita los botones cuando recibe ids.
- Alta y K: rótulos en minúscula y atenuados en la columna 1, campos desde la
  columna 12 en color de foco, error en rojo y nota «Crear guarda la definición».
  El origen sigue siendo una casilla y no los botones de opción del boceto T.
- Verificado: compilación y 152 comprobaciones del dashboard. Sin recorrido
  visual ni publicación Native AOT tras este ajuste.

### Corrección del recorrido tras la revisión del usuario — 2026-11-10

- Alta (T): el servicio sólo elige un backend ya preparado en su ámbito; no
  descarga, no registra externos ni prepara. Sin backends, «Agregar» queda
  deshabilitado y «Ir a backends» abre Backends del mismo ámbito. Crear usa
  `EnsureGlobalAsync(installed: true)`, sin volver a resolver la variante en red.
- K queda como único lugar para descargar o registrar un backend.
- Descargas: la TUI no escribe en la consola (el núcleo informaba el progreso por
  `Console.Error` y rompía la pantalla); N se vuelve a leer en cada intervalo.
- Fin de operaciones de gestión: sólo aviso en Notificaciones, con detalle al abrirlo.
- Pendiente de decisión: alta de un servicio local con el nombre del proyecto en
  sólo lectura. La CLI sólo crea servicios globales (`service create --global`).
- Backends: se retira «Descargar versión…», duplicado de «+ Backend» (boceto O y
  vista previa actualizados).
- Cada aviso nuevo emite la campana de la terminal (BEL); sonido o destello
  dependen de la terminal. Sin verificar en una terminal real.

### Hallazgos de la revisión del usuario — 2026-11-10

Registrados para evaluación en [009](../../docs/findings/009-cambiar-backend-servicio.md),
[010](../../docs/findings/010-descargas-globales.md) y
[011](../../docs/findings/011-cancelar-descarga-tui.md).

### Cierre — 2026-11-10

El usuario validó el recorrido y dio la tarea por terminada. Quedan registrados como
hallazgos 009–011; la suite general sigue deteniéndose en `help fits 80 columns`.
