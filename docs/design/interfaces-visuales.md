# Interfaces visuales

## Panel del proyecto

TUI y Web UI son frontends de las mismas operaciones y estado que CLI.
El alta de archivos locales y matching opcional usan el inventario del proyecto,
incluidas copias traídas de otro gestor. No requieren descargar de nuevo ni mantienen
una biblioteca global paralela. Las preferencias de Coquena se distinguen de la
configuración del servidor y usan el ámbito global de config.
Abiertas en una carpeta muestran ese proyecto, su servicio destino y composición publicada.
Una carpeta con `coquena.yaml` ofrece su nodo local aunque todavía no exista
estado en `.coquena/`. El glifo local distingue el proyecto de los servicios
globales; la etiqueta usa `name` de la receta o, si falta, el nombre de la carpeta.
El nodo existe también para una receta consumidora sin servicio propio.
Abrirla no prepara recursos, inicia procesos ni publica presets.

Dentro del proyecto, Up publica los presets de la receta en su destino y Down
retira sus publicaciones registradas, con las mismas reglas que la CLI. El
destino se muestra separado del servicio propio: declarar un servicio no cambia
el target de publicación. Si la receta declara servicio, se ofrecen sus controles
de ciclo y sus logs. Los controles de publicación se distinguen de la carga o
descarga de un modelo en memoria y de la transferencia de archivos.
La opción «Preparación privada» equivale a `--private`: mantiene la preparación
en el estado local y permite las referencias privadas bajo las reglas de
[persistencia](persistencia-proyectos.md). Se elige explícitamente para las
acciones de inicio y Up de esa sesión; no modifica la receta.
Navegar a otros proyectos conocidos de la misma máquina es posible sin introducir
procesos por consumidor ni gestión multi-host implícita.

Panel muestra salud, proceso, logs, métricas/gráficos y notificaciones. Un dashboard
de consulta puede existir sin editor ni acciones de control. Los formularios y
acciones son capacidades diferenciadas; compartir contratos no obliga a mostrarlas
todas en cualquier vista.

Abrir no inicia automáticamente; cerrar no detiene. Las acciones de ciclo del servicio usan
modo daemon: primero recuperación automática cuando corresponda, luego notificación
accionable. El panel no intercepta fallback para exigir decisión interactiva.
Las publicaciones de presets son acciones interactivas independientes: comprueban
la actividad y, si hay peticiones activas o en cola, permiten continuar aceptando
el riesgo o cancelar. Si no puede comprobarse la actividad, la publicación se
detiene, según [servidores](servidores.md#plazos-y-control). Que el servicio esté desacoplado
no acepta automáticamente ese riesgo.
Observación en [observabilidad](observabilidad.md).

## Espacios de trabajo

Al abrir la TUI desde una receta local reconocida, el proyecto queda
disponible para navegación mediante el índice de proyectos de Coquena.
El árbol de Definiciones lista el ámbito Global y los proyectos conocidos; sólo
el enfocado se expande. El índice global localiza los catálogos de proyecto;
cada catálogo conserva su inventario, definiciones e identidad. Una ruta que ya
no se puede resolver se muestra como inaccesible.

Enfocar otro ámbito cambia el catálogo que se consulta, sus transferencias y los
servicios propios que se ofrecen. Los servidores globales siguen disponibles.
Cambiar de ámbito conserva las transferencias y pruebas en curso, respeta el
borrador de cada receta y mantiene los servidores en su estado actual. Iniciar o
detener un servidor y publicar o retirar una prueba requieren acciones explícitas.

## Barra de estado de la máquina

La TUI mantiene dos filas de hardware en el borde inferior en todos los
espacios y pestañas. La primera resume CPU y RAM de la máquina; la segunda
identifica cada GPU y muestra uso, memoria dedicada usada/capacidad y memoria
compartida. Cambiar de catálogo o servidor conserva la misma serie de muestras.
Los datos ausentes o vencidos se identifican como tales. El detalle Hardware
se abre por teclado o clic desde la barra y permite consultar cada dispositivo,
sus gráficas y las fuentes de las mediciones. Cuando falta ancho se compactan
las unidades; el detalle conserva los valores, unidades y motivos completos.
La memoria abreviada con G expresa GiB, conforme al [glosario](glosario.md).

La barra indica consumo de máquina; no atribuye GPU ni memoria compartida a
un servicio o modelo. El monitor conserva fases y tasas por entrada o tarea.

## Dashboards de observación

La TUI organiza la navegación en ● En vivo, ◇ Definiciones y ≡ Configuración,
sin marca ni fila de atajos. En vivo agrupa servicios y la receta local en un
árbol lateral; Definiciones consulta la información de definiciones disponible
sin acciones de modificación. Configuración separa preferencias de TUI del
inventario y sondeo de Sistema. La [consulta de definiciones](tui/B-definiciones-recetas.salt.puml)
conserva el catálogo observado. La [vista por servidor](ui-servicio.md) muestra inferencia en
curso, rendimiento, recursos compartidos y logs. El log queda accesible dentro
del servidor aunque el espacio vertical obligue a separarlo del monitor.

Un indicador sin fuente disponible muestra «—» y no ofrece una acción ni
presenta un recuento inferido. El [detalle de hardware](tui/D-hardware.salt.puml)
conserva el alcance y las fuentes de los dispositivos medidos.

Las pestañas descubren servicios mediante el registro local. Los fallos de
una instancia se aíslan de las demás. Los gráficos conservan unidades,
distinguen muestras ausentes o desactualizadas y se adaptan a la terminal.
El historial visual es de sesión. Las frecuencias de captura y de refresco
siguen [observabilidad](observabilidad.md).

Las herramientas de autoría y configuración conservan estas responsabilidades:

- [Presets](tui/P-definiciones-presets.salt.puml) lista la contribución del proyecto y abre el
  editor con comparación entre guardado, activo y borrador.
- Buscar modelos reúne [inventario local](tui/W-definiciones-modelos-inventario.salt.puml) y
  [búsqueda y descarga](tui/C-definiciones-modelos.salt.puml). Permite consultar el
  README, elegir variante y preparar un modelo para un preset.
- [Configuración](tui/E-configuracion-coquena.salt.puml) agrupa preferencias de Coquena.
  Los argumentos del backend se editan como parte de la definición del proyecto.

La navegación de terminal distingue pestaña activa y foco de teclado. Usa la
[paleta de Coquena](../../docs-site/assets/coquena.css): terracota para selección,
turquesa exclusivamente para foco y verde salvia para éxito. Prompt usa azul;
generación y recursos usan oro. Advertencia y error conservan ámbar y rojo,
acompañados de texto. Las zonas se distinguen por fondo (base para contenido,
superficie para navegación y hardware, tarjeta para títulos), sin bordes ni
líneas. Los botones se distinguen por fondo y texto, con turquesa al enfocar.
En 16 colores se usan rojo para selección, cian para foco, verde para éxito,
azul para prompt y amarillo para generación; los estados conservan etiquetas.
`NO_COLOR` conserva toda la jerarquía textual y las mismas acciones.

El mínimo de diseño es 80×24. Al aumentar la terminal, el espacio adicional va
a contenido, gráficas, tablas y logs; la cabecera y las dos filas de hardware
mantienen su altura. Las zonas desplazables permiten consultar el contenido
sin superponer controles. Los modales presentan título, contenido y acciones
explícitas por fondo, con Aceptar/Cancelar o las opciones de la operación. Los wireframes muestran
la jerarquía y los recorridos con datos ilustrativos; el conjunto está en
[pantallas de la TUI](tui/README.md).

## Acciones compartidas con la CLI

Los controles visuales invocan las mismas operaciones de aplicación que la
CLI, con el ámbito y el recurso enfocado explícitos. Cada acción disponible
por clic tiene una forma de enfocarla e invocarla por teclado. Antes de
confirmar una operación se muestra el destino y, cuando corresponda, el plan
y los riesgos. Las restricciones y confirmaciones son las del comando.

Configuración lee el valor efectivo al abrir y después de guardar o
restablecer. Guardar asigna el valor elegido y restablecer elimina el override
de esa preferencia. Repetir cualquiera deja el mismo valor efectivo y no
relanza observadores ni fuerza limpieza. La edición conserva las unidades,
validaciones y valores predeterminados de CLI. La reconexión se guarda como
un par de valores. La retención global se distingue del override YAML de
cada servicio. Si el valor cambió externamente durante la edición, se muestra
el valor vigente antes de sobrescribir.

## Selección, activo y comparación

Permitir elegir archivos YAML y su orden para el siguiente inicio/apply.
Leer combinación ACTIVA de registro de instancia; no inferir desde selección actual.
Comparar backend, modelos, companions y parámetros efectivos con lo realmente activo.
Mostrar además el nombre de receta y nombres registrados frente a la intención actual:
una carpeta renombrada no modifica las entidades creadas. Una diferencia de nombres
se resuelve mediante baja y alta explícitas, no como efecto de guardar el YAML.
Resaltar diferencias con color/fondo; etiquetas/estado deben hacer comprensible
qué está seleccionado, guardado, aplicado y pendiente.

Guardar o elegir otra combinación no cambia instancia por sí solo.
Al guardar override nuevo, seleccionarlo como última capa para próximas acciones,
sin aplicar ni reiniciar automáticamente.

## Formularios de parámetros

Schema del proyecto aporta nombre, tipo, ámbito, aplicabilidad, descripción,
opciones/default y dependencias cuando el validador las soporte.
Sin schema mantener editor genérico de args. La CLI también permite mantener coherencia.
No convertir configuración de parámetros en requisito diario para usar el panel.

## Borradores y guardado

Cada sesión/instancia TUI o Web que edita conserva copia de origen y borrador vinculado
al YAML físico concreto. Persistir automáticamente el borrador en metadatos locales
ignorados por Git: temporal significa copia de trabajo, no eliminación al cerrar.
Reabrir recupera cambios sin obligar a escribir el YAML definitivo.
No mezclar ni sobrescribir silenciosamente borradores de distintas sesiones.

Guardar valida y publica al archivo elegido; también permite nuevo override con
solo diferencias, incluidas desasignaciones mediante clave sin valor.
Salir con cambios ofrece guardar, descartar o cancelar, incorporando la posibilidad
de conservar borrador recuperable sin guardar definitivo.

Antes de guardar comparar archivo en disco con versión DE ORIGEN, no con borrador
modificado. Si cambió externamente, elegir descartar borrador/recargar o sobrescribir
disco con memoria. La edición puede continuar hasta ese momento; no exigir fusión
automática ni bloquearla por adelantado.

## Experimentación en vivo

El usuario puede escribir y editar la receta YAML en un editor externo, publicarla
con `up` y abrir el panel para observar cómo se comporta el modelo durante las
peticiones. Puede cerrar el panel, retirar con `down`, editar la receta, aplicar
otra vez con `up` y volver a observar. También puede mantener el panel abierto
mientras ejecuta esos comandos desde otra terminal. El panel reconcilia los
cambios publicados y distingue las generaciones e instancias observadas.
Editar el YAML por sí solo no cambia el preset activo.

El recorrido visual empieza con un modelo y un uso a probar. El usuario prepara
un preset con parámetros y, si corresponde, un companion compatible. Publica la
prueba explícitamente en el router en ejecución y sigue esa entrada en la pestaña
del servidor. Allí puede comprobar carga, fases, avance, tasas y logs durante
una petición de prueba. La publicación y el retiro en caliente siguen las reglas
de [servidores](servidores.md#plazos-y-control). Cargar o descargar de memoria
es una operación distinta de publicar o retirar una entrada.

La calidad de la salida se evalúa en el cliente que hizo la prueba. El monitor
aporta evidencia de rendimiento y diagnóstico para decidir si ajustar el preset,
probar otro companion, guardar la receta YAML o retirar la prueba. El usuario
evalúa cada intento con las cifras observadas, su fuente, alcance y ventana.
Este recorrido no requiere un comparador de ejecuciones o configuraciones en
el panel ni conservar el histórico entre aperturas.


Buscar/explorar modelos, leer README, seleccionar/descargar y crear preset de prueba.
Aplicar explícitamente antes de guardar YAML cuando backend lo soporte, observar
carga/resultado, y persistir receta si convence. Una representación operativa local
puede alimentar el router; no presume que acepte definiciones completas por HTTP.

Distinguir borrador no aplicado, prueba activa y receta guardada. El estado de edición
no sustituye consulta del servidor. Una prueba puede seguir activa al cerrar interfaz,
sin restauración/detención automática por cierre.
Por defecto no se promueve a recuperación local hasta estar respaldada por YAML guardado;
opción de proyecto permite último borrador aplicado y funcional.

Tras editar preset, ofrecer aplicar ahora; aceptar es acción explícita.
Retirar capa de experimentación y aplicar
permite volver al productivo sin borrar sus archivos.
Cambiar backend requiere reiniciar; preservar recursos y vida de instancia según
[servidores](servidores.md).

## Recuperación y decisiones del usuario

Fallo de reinicio puede recuperar automáticamente según modo daemon del ciclo
del servicio. Fallo de publicación intenta restauración acotada según
[servidores](servidores.md#restauración-acotada-de-presets), independientemente de
la decisión interactiva que autorizó publicar.
Mostrar qué falló, qué configuración sigue funcionando y acciones posibles.
El usuario actúa luego por tecla/clic para corregir, detener o reintentar.
No aplicar cambios ocultos por cerrar una notificación.

## Contratos por concretar

Atajos definitivos, selección de varios borradores, conflicto entre
sesiones y ciclo de restauración experimental requieren diseño. La Web UI debe
concretar acceso, autenticación y despliegue; que observe un servidor no autoriza
exponer controles de escritura indiscriminadamente en red.
