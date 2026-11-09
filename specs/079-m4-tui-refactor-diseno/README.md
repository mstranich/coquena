---
status: planned
created: 2026-11-09
priority: high
tags:
- tui
- dashboard
- m4
created_at: 2026-11-09T16:32:45.882585700Z
updated_at: 2026-11-09T18:21:48.055670500Z
transitions:
- status: in-progress
  at: 2026-11-09T16:49:15.382420800Z
- status: planned
  at: 2026-11-09T18:21:48.055670500Z
---
# Refactorizar las pantallas existentes de la TUI al nuevo diseño

## Objetivo

Llevar al nuevo diseño las pantallas que la TUI ya ofrece, sin agregar secciones
nuevas. Las vistas y secciones que todavía no existen (Definiciones por ámbito,
Editor, Descargas, Backends, Actualizaciones, Mantenimiento, etc.) se entregan en
tarjetas posteriores, una por vista o sección. Entrega de
[M4](../../docs/milestones/M4-experiencia-completa.md).

## Fuentes

- Propuesta: [docs/propuestas/tui-rediseno](../../docs/propuestas/tui-rediseno/README.md)
  y su [vista previa](../../docs/propuestas/tui-rediseno/vista-previa.html).
  Es un borrador; sólo pasa a regla al promoverse a `docs/design/`.
- Reglas vigentes: [interfaces-visuales](../../docs/design/interfaces-visuales.md),
  [ui-servicio](../../docs/design/ui-servicio.md),
  [observabilidad](../../docs/design/observabilidad.md),
  [privacidad](../../docs/design/privacidad.md).
- Base entregada: [066](../066-m3-tui-base/README.md) a
  [070](../070-m3-dashboard-integrado/README.md).

## Pantallas actuales y destino

Inventario inicial de `src/Coquena/DashboardCli.cs` y su correspondencia con la propuesta.

| Pantalla actual | Contenido | Destino en la propuesta |
|---|---|---|
| Ventana y fila de pestañas por servicio (`◎`/`◇`), Sistema, Configuración | Una pestaña por servicio global o proyecto local | Pestañas ● En vivo · ◇ Definiciones · ≡ Configuración e indicadores ⇣/▲ (Definiciones sólo consulta contenido ya disponible) |
| Barra inferior de una fila (CPU, RAM, GPUn, VRAMn) | Hardware de la máquina | Barra de dos filas: CPU, RAM y por GPU uso, dedicada y compartida (`gpu.shared` ya se captura) |
| Panel del servicio: Iniciar/Detener/Reiniciar y estado | Control de ciclo | A · fila del servicio en En vivo |
| Resumen de entradas, tareas, gráficos de fase, prompt, generación, CPU y RAM | Monitor de inferencia | A · árbol servicio → proyecto → preset y detalle de la entrada (tasas, caché, especulación, contexto por slot, tareas) |
| Subpestaña Presets (lista de entradas del catálogo) | Entradas publicadas | A · nodos del árbol |
| Diálogos de detalle de tarea y de entrada | Métricas del backend | A · detalle de la entrada |
| Subpestaña Logs: Pausar, Actual/seguir, Históricos | Log del servicio | A · log del servicio con Pausar/Seguir/Histórico |
| Diálogo Históricos (lista de archivos) | Logs por instancia | I · log histórico, sin la búsqueda RegEx (otra tarjeta) |
| Subpestaña Recursos (volcado de métricas) | Hardware y proceso | D · hardware ampliado por dispositivo y CPU/RAM del proceso del servicio |
| Pestaña Sistema (sondeo y adaptadores) | `system probe` | G · Configuración › Sistema, con versión |
| Pestaña Configuración (filtrar logging de monitoreo) | Preferencia de la TUI | F · Configuración › TUI (filtro del log) |
| Confirmaciones y resultados (`MessageBox`, publicación, selección de recurso) | Mensajes con acciones | Modales Aceptar/Cancelar o Sí/No/Cancelar con el estilo nuevo |
| Up/Down, destino y Preparación privada en la pestaña local | Publicación de la receta | En vivo · bloque de receta local, separado del ciclo del servicio |

## Trabajo

- [x] Promover a `docs/design/` las reglas de la propuesta que afectan a estas
  pantallas (pestañas, zonas por fondo, barra de hardware, unidades de dato) y
  reemplazar los wireframes Salt correspondientes; señalar cada cambio de regla.
- [x] Aplicar la paleta como tema: roles de color de la propuesta, verde de éxito
  separado del turquesa de foco, zonas por fondo sin bordes ni líneas, botones por
  fondo y texto. Definir el comportamiento con 16 colores.
- [x] Estructura de pestañas e indicadores, sin marca ni fila de atajos.
- [x] En vivo (A): árbol servicio → proyecto → preset, fila del servicio con
  puerto, estado, CPU/RAM del proceso y controles de ciclo; detalle de la
  entrada con su cola observable; log del servicio con Pausar, Seguir e Histórico.
- [x] Log histórico (I) con la navegación por instancia que ya existe.
- [x] Hardware: barra de dos filas y vista ampliada (D) sólo con datos medidos.
- [x] Configuración: categorías TUI (filtro del log) y Sistema (sondeo y versión).
- [x] Modales existentes con el estilo nuevo.
- [x] Layout mínimo 80×24; en terminales mayores el espacio libre va a las zonas de
  contenido.
- [x] Actualizar `src/code-map.yaml` para las rutas afectadas.

## Fuera de alcance

Secciones nuevas de Definiciones (Recetas, Presets, Servicios, Modelos, Backends,
Parámetros, Descargas, Actualizaciones, Mantenimiento), Editor, Configuración ›
Coquena, selección de recursos de la barra, búsqueda RegEx en el histórico y
notificaciones con contenido. Se acordarán en tarjetas posteriores.

## Criterio acordado

- Definiciones es de sólo lectura: reorganiza exclusivamente la información que
  ya mostraba la TUI (servicio/receta, destino y catálogo observado). Sin altas,
  edición, publicaciones ni inventarios nuevos.
- Up/Down y Preparación privada se conservan en un bloque de receta local de
  En vivo, separado de los controles del proceso. El destino sigue explícito.
- El refactor debe aportar cohesión y jerarquía, conservar todos los elementos
  existentes y establecer una base para ampliar las vistas posteriormente.
- Los prefijos del árbol son espacios de nombres publicados, no atribución de
  clientes. Se conserva el nombre completo como identidad de selección.

## Aceptación

- [ ] Las pantallas de la tabla muestran, en el nuevo diseño, al menos la misma
  información y acciones que antes; nada se pierde en el traslado.
- [ ] Fidelidad con las pantallas A, D, F, G e I de la propuesta: cada zona
  (pestañas, árbol, fila del servicio, detalle de la entrada, log, barra de
  hardware) es una vista propia con su presentación probada, en la posición y
  jerarquía de la vista previa; el log es visible en En vivo sin abrirlo.
- [ ] Lo que no tiene medición no ocupa filas: sin bloques de «n/d» ni gráficas
  vacías; un único estado breve indica la ausencia de señal.
- [ ] Sin controles duplicados ni subpestañas ocultas residuales del diseño
  anterior.
- [ ] 80×24 sin desbordes, truncamientos de identidad sin indicar ni restos de
  repintado; terminal mayor aprovecha el espacio.
- [ ] Pruebas pertinentes actualizadas y en verde.
- [ ] Publicación Native AOT win-x64 y ejecución del binario en terminal real con
  un servicio en inferencia; capturas anonimizadas a 80×24 y a un tamaño mayor,
  revisadas con el usuario.

## Revisión 2026-11-09

La primera implementación reacomodó los componentes previos sin las zonas de la
propuesta (log oculto tras un botón, bloques de «n/d», gráficas vacías, controles
duplicados, restos de repintado a 80×24). Se rehace por zonas; la evidencia
siguiente corresponde a esa primera implementación.

## Implementación y evidencia — 2026-11-09

Implementación y validación técnica listas. La tarjeta permanece `in-progress`
hasta la revisión de UX con el usuario prevista en M4; la primera casilla de
aceptación conserva ese contraste visual. Sin commit ni push. No se actualizó
la herramienta instalada globalmente.

### Reorganización

- En vivo: navegación lateral servicio → prefijo publicado → entrada; cabecera
  del servicio, bloque separado de receta, resumen de entrada, curvas por tarea,
  tareas activas/recientes y log. Se retiró la lista duplicada del catálogo.
- Definiciones: consulta de los datos ya disponibles, con destino y catálogo
  observado; no inicia recursos ni ofrece acciones de modificación. La fecha
  del catálogo y la marca de antigüedad separan observación de intención.
- Hardware: dos filas permanentes y detalle por CPU/RAM o GPU confirmada. La
  barra compacta dedicada/compartida sin atribuirlas al servicio; indica con
  puntos suspensivos cuando el resumen supera el ancho. El detalle conserva
  fuentes, fechas, ventanas y motivos, y las curvas conservan sus huecos.
- Configuración: TUI y Sistema. Se conserva el filtro y el sondeo, y Sistema
  identifica la versión del ejecutable. Los indicadores superiores sin fuente
  muestran «—», sin números inventados ni navegación a funciones nuevas.
- Tema y gráficas se separan en `DashboardVisuals.cs`; navegación y resúmenes
  en `DashboardPresentation.cs`. Se conservan las operaciones compartidas con
  CLI y los límites del observador. Las filas compactas identifican slot/tarea.
- Se promueven las reglas de navegación, jerarquía, zonas, paleta y hardware en
  los documentos responsables, y se sustituyen/agregan seis wireframes. Son
  decisiones de diseño autorizadas, no cambios a los contratos de métricas o
  ciclo. La propuesta conserva sus vistas adicionales como borrador.

### Validación ejecutada

Producto y runner publicados Native AOT win-x64 con advertencias AOT/trimming
tratadas como errores. Publicación final sin advertencias ni errores. Un build
administrado intermedio reintentó copiar un ejecutable abierto durante el ensayo;
se cerró esa instancia antes de las publicaciones finales.

Suites ejecutadas con el runner nativo y el producto final:

| Suite | Comprobaciones |
|---|---:|
| Dashboard | 72 |
| Publicación | 50 |
| Observación | 68 |
| Seguimiento de logs | 71 |
| Servicios por ámbito | 39 |

- PTY Windows 80×24 y 120×40, incluido resize durante la sesión. Se observaron
  monitor, log ampliado, definición sin acciones, hardware por dispositivo,
  categorías de configuración y el selector de históricos. En terminal mayor
  monitor y log comparten el contenido; la barra conserva exactamente dos filas.
- Inicio y Up desde la TUI sobre una receta aislada con backend externo local
  b12058 / 3f8af15a7 y stories15M-q4_0.gguf, sin descargas. El catálogo remoto
  confirmó la publicación. Peticiones sintéticas externas permitieron observar
  acumulados, tareas y sus identidades con el producto AOT final.
- El selector abrió un archivo registrado como histórico de lectura fija. Se
  consultó la confirmación de reinicio y se canceló; el servicio conservó salud
  HTTP 200. Los controles de log y su captura permanecen separados del monitor.
- Prueba explícita de 16 colores: selección, foco y éxito usan roles distintos;
  la salida capturada contiene sus secuencias de color, sin RGB de 24 bits. El
  driver negociado también usó 16 colores en modo automático: no se acredita
  una reproducción visual independiente de truecolor.
- `NO_COLOR`: receta consumidora sin estado previo, sin crear `.coquena/`, con
  destino global y las acciones existentes accesibles. Sin desbordes observados.
- Las sesiones finales salieron con código 0 y restauraron la terminal. Cerrar
  la TUI conservó salud HTTP 200 y el hash del YAML. Después se detuvo
  explícitamente el servicio aislado; no se operaron servicios del usuario.
- Se comprobaron 50 enlaces locales, 316 referencias del mapa y `git diff --check`.
  Los seis Salt se validaron y renderizaron a SVG con PlantUML 1.2026.8. No se
  modificaron ejemplos JSON documentales ni el manual de la versión publicada.

La evidencia se redacta anonimizada; logs y capturas crudos permanecen fuera de
Git. Las peticiones individuales fueron más breves que el período de captura:
no se acredita una curva sostenida por tarea, porcentaje ni ETA. No se ensayó
la selección remota de múltiples recursos ni otras plataformas. El ensayo
por PTY no sustituye el contraste visual y de utilidad con el usuario.

### Reproducción

Definir `$TestRoot` como un directorio de prueba nuevo, `$BackendExe` como el
build externo local contrastado y `$SmallModel` como el GGUF local. El setup
prepara la receta y su registro; no inicia el servicio.

~~~powershell
dotnet publish src/Coquena -c Release -r win-x64 --no-restore -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 --no-restore -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-tests
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-project-setup "$TestRoot/news" $BackendExe $SmallModel
$env:COQUENA_HOME = "$TestRoot/home"
Set-Location "$TestRoot/news"
& "$TestRoot/product/coquena.exe" tui
~~~

Elegir Preparación privada para esas referencias externas; recorrer Iniciar,
Up, En vivo, Definiciones, Hardware, Configuración y Logs. Enviar peticiones
sintéticas desde otro cliente. Histórico elige una instancia registrada;
Actual / seguir vuelve al log vigente. Al terminar, cerrar la TUI y detener
explícitamente el servicio de prueba con `service self stop`.

### Aspectos para revisión

El árbol agrupa por prefijo del nombre publicado; ese dato no demuestra el
proyecto propietario ni quién envió una petición. El nombre completo y el
servicio son la identidad de selección. Al incorporar las vistas por ámbito
del horizonte de M4, la atribución deberá contrastarse con el registro, sin
convertir el prefijo visual en una política de propiedad.

La cola conservada es la disponible por entrada. La cabecera muestra CPU y RAM
privada verificadas del servicio; no agrega tasas medias ni atribuye GPU. Es
una decisión resuelta por las reglas de observabilidad, no una fuente nueva.
El usuario todavía debe evaluar cohesión, legibilidad y navegación del diseño;
esa evaluación no se reemplaza por las comprobaciones automáticas.
