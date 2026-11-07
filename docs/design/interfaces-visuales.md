# Interfaces visuales

## Panel del proyecto

TUI y Web UI son frontends de las mismas operaciones y estado que CLI.
El alta de archivos locales y matching opcional usan el inventario del proyecto,
incluidas copias traídas de otro gestor. No requieren descargar de nuevo ni mantienen
una biblioteca global paralela. Las preferencias de Coquena se distinguen de la
configuración del servidor y usan el ámbito global de config.
Abiertas en una carpeta muestran ese proyecto, su servicio destino y composición publicada.
Navegar a otros proyectos conocidos de la misma máquina es posible sin introducir
procesos por consumidor ni gestión multi-host implícita.

Panel muestra salud, proceso, logs, métricas/gráficos y notificaciones. Un dashboard
de consulta puede existir sin editor ni acciones de control. Los formularios y
acciones son capacidades diferenciadas; compartir contratos no obliga a mostrarlas
todas en cualquier vista.

Abrir no inicia automáticamente; cerrar no detiene. Las acciones de ciclo del servicio usan
modo daemon: primero recuperación automática cuando corresponda, luego notificación
accionable. El panel no intercepta fallback para exigir decisión interactiva.
Las publicaciones de presets son acciones interactivas independientes: muestran
ocupación conocida o desconocida y permiten continuar aceptando el riesgo o cancelar,
según [servidores](servidores.md#plazos-y-control). Que el servicio esté desacoplado
no acepta automáticamente ese riesgo.
Observación en [observabilidad](observabilidad.md).

## Espacios de trabajo

Al abrir la TUI desde una receta local reconocida, el proyecto queda
disponible para navegación mediante el índice de proyectos de Coquena.
El selector ofrece el catálogo global y los proyectos conocidos. El índice
global localiza los catálogos de proyecto; cada catálogo conserva su
inventario, definiciones e identidad. Una ruta que ya no se puede resolver
se muestra como inaccesible.

Cambiar de espacio cambia el catálogo editable, sus transferencias y las
pestañas de servicios propios que se ofrecen. Los servidores globales siguen
disponibles. La cabecera distingue espacio activo, servidor enfocado y
destino de publicación de la receta. Cambiar de espacio conserva las
transferencias y pruebas en curso, respeta el borrador de cada receta y
mantiene los servidores en su estado actual. Iniciar o detener un servidor
y publicar o retirar una prueba requieren acciones explícitas.

El [selector de espacios](tui-espacios.salt.puml) es accesible desde cualquier
pestaña. Permite volver a un proyecto y operar sobre sus modelos y presets,
publicar en el global compartido o usar el servidor propio con su backend.

## Barra de estado de la máquina

La TUI mantiene una barra visible en el borde inferior en todos los espacios y
pestañas, incluido el selector de espacios. Resume CPU, GPU, RAM y VRAM de la
máquina. Cambiar de catálogo o servidor conserva la misma serie de muestras.
Con varias GPU, identifica el dispositivo resumido y señala si hay otros;
el monitor muestra el detalle por dispositivo. Los datos ausentes o vencidos
se identifican como tales.

Con espacio y una fuente disponible, puede incluir actividad de una unidad de
almacenamiento identificada y un resumen del servidor observado. El resumen
identifica el servidor y muestra su actividad agregada, por ejemplo cuántas
entradas están activas y cuántas peticiones esperan. Si falta ancho se retiran
primero esos campos opcionales y se compactan las etiquetas de hardware.
La barra indica el consumo de la máquina; el monitor conserva las series y
las fases por entrada o tarea.

## Dashboards de observación

La TUI ordena sus pestañas por servidores globales, servidores del proyecto y
herramientas. La [vista por servidor](ui-servicio.md) muestra inferencia en
curso, rendimiento, recursos compartidos y logs. El log queda accesible dentro
del servidor aunque el espacio vertical obligue a separarlo del monitor.

Las pestañas descubren servicios mediante el registro local. Los fallos de
una instancia se aíslan de las demás. Los gráficos conservan unidades,
distinguen muestras ausentes o desactualizadas y se adaptan a la terminal.
El historial visual es de sesión. Las frecuencias de captura y de refresco
siguen [observabilidad](observabilidad.md).

Las herramientas aparecen al final:

- [Presets](tui-presets.salt.puml) lista la contribución del proyecto y abre el
  editor con comparación entre guardado, activo y borrador.
- Buscar modelos reúne [inventario local](tui-modelos.listado.salt.puml) y
  [búsqueda y descarga](tui-modelos.descarga.salt.puml). Permite consultar el
  README, elegir variante y preparar un modelo para un preset.
- [Configuración](tui-configuracion.salt.puml) agrupa preferencias de Coquena.
  Los argumentos del backend se editan como parte de la definición del proyecto.

La navegación de terminal distingue pestaña activa y foco de teclado. Usa la
[paleta de Coquena](../../docs-site/assets/coquena.css), con terracota para selección,
turquesa como acento y etiquetas legibles aun sin color. Los wireframes muestran
la jerarquía y los recorridos con datos ilustrativos.

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
