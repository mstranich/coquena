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

Abrir no inicia automáticamente; cerrar no detiene. Las acciones de ejecución usan
modo daemon: primero recuperación automática cuando corresponda, luego notificación
accionable. El panel no intercepta fallback para exigir decisión interactiva.
Observación en [observabilidad](observabilidad.md).

## Dashboards de observación

La TUI ofrece dos vistas navegables, sin requerir un editor:

- General: resumen compacto de todos los servidores locales registrados por Coquena
  cuya identidad se pueda comprobar. Muestra proyecto, salud, entradas cargadas,
  tasas y recursos disponibles, sin panel de logs por servidor.
- Servicio seleccionado: pantalla completa con gráficos temporales y cifras arriba y
  seguimiento de logs abajo. Permite identificar la instancia y el modelo/entrada
  observado; si hay varios cargados, no presupone un único modelo actual.

El detalle permite seleccionar servicios globales o propios de proyectos. La cabecera
mantiene visibles identidad, salud, entradas activas, fase de actividad y progreso
disponible aun en disposición compacta. Al reducir espacio se simplifican gráficos
y columnas secundarias antes de ocultar esas cifras. El log ocupa la zona inferior;
ampliarlo o pausarlo visualmente no detiene la captura ni las peticiones.

La pantalla Servicios muestra consumo de la máquina por dispositivo arriba,
actividad por entrada/slot/tarea en
el centro y logs con selección de servicio abajo. La tabla permite distinguir
actividad en cola, procesamiento de prompt, generación y resultados recientes,
cuando las fuentes permitan identificar esos estados y conservar su historial.
La selección del log no filtra implícitamente el consumo global de hardware.
La [semántica de la pantalla Servicios](ui-servicio.md) define distribución vertical,
identificación por servicio/slot/tarea, histórico visual configurable y espacio de logs.
Los wireframes de Modelos muestran [listado](tui-modelos.listado.salt.puml) y
[descarga](tui-modelos.descarga.salt.puml).
Slots/tareas se muestran por instancia y entrada, con fase, tokens de
prompt procesados/cacheados, tokens generados y tiempos según evidencia disponible.
Una estimación de tiempo restante se identifica como tal; no equivale al tiempo
medido hasta el primer token. Datos ausentes o desactualizados se indican sin
inventar cero ni deducir inactividad de un log silencioso. El refresco visual de
referencia es de un segundo, independiente de la frecuencia real de cada fuente.

Las gráficas usan caracteres de terminal (Unicode, braille o ASCII), se adaptan al
espacio disponible y conservan etiquetas/unidades y alternativa legible sin color.
Se observan CPU, RAM, GPU, VRAM, procesamiento de prompt y generación según las
fuentes y disponibilidad descritas en observabilidad. El historial es acotado a la
sesión, no una plataforma de monitoreo persistente. Los logs tienen seguimiento y
scroll/pausa de visualización sin pausar ni detener el servidor.

La vista general descubre instancias mediante el registro local de proyectos y servicios;
no recorre discos ni mezcla inventarios de recursos en un catálogo global. Puede
abrirse desde un proyecto y cambiar de vista sin cambiar intención ni combinación.
Los fallos de una instancia no bloquean la actualización de las demás. No incluye
procesos ajenos sin registro ni gestión multi-host.

Buscar modelos, combinar modelo/preset y editar recetas son capacidades separadas
de observar. La experiencia de edición final corresponde a TUI/Web UI; prioriza
selección de modelos y presets antes que formularios avanzados de argumentos.

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

Fallo de reinicio/apply puede recuperar automáticamente según modo daemon.
Mostrar qué falló, qué configuración sigue funcionando y acciones posibles.
El usuario actúa luego por tecla/clic para corregir, detener o reintentar.
No aplicar cambios ocultos por cerrar una notificación.

## Contratos por concretar

Distribución visual, paleta/atajos, selección de varios borradores, conflicto entre
sesiones y ciclo de restauración experimental requieren diseño. La Web UI debe
concretar acceso, autenticación y despliegue; que observe un servidor no autoriza
exponer controles de escritura indiscriminadamente en red.
