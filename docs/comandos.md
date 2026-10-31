# Comandos de la CLI

## Convenciones

Los comandos distinguen ámbito de intención (proyecto o global), servicio destino
y efecto. config administra preferencias globales. La gramática aquí es contrato;
la disponibilidad real se acredita por la entrega del ejecutable.
Sin selección explícita se usa `coquena.yaml`. El [formato del proyecto](formato-proyecto.md) concreta campos, layout e inicialización.
Los archivos adicionales se combinan en orden; para editar se modifica sólo la
última capa seleccionada, validando antes y después. Consultar o editar no inicia
servidores ni aplica cambios a una instancia.

Las rutas relativas comparten base de proyecto, no la carpeta de cada override.
La selección sigue la referencia de Compose: archivos -f ordenados y base en el
primero; una selección explícita de directorio permite cambiar esa base.
La selección y el nombre `coquena.yaml` se concretan en [Formato del proyecto](formato-proyecto.md).

No descargar gigabytes ni recalcular hashes sólo por listar estado.
Distinguir errores de configuración, consulta remota, compatibilidad, integridad,
transferencia y ejecución. Informar acciones posibles sin cambiar intención
automáticamente. No guardar secretos expandidos en archivos compartidos.

## Mapa por comando

### Ámbito y destino del estado

| Familia | Ámbito y efecto |
| --- | --- |
| config | Preferencias globales de Coquena; no requiere receta. |
| system probe | Consulta del entorno; no requiere receta. |
| --help, -h, help | Consulta de ayuda de la CLI; no requiere receta. |
| init | Inicialización opcional de la carpeta/receta seleccionada. |
| import | Copia explícita de intención al ámbito global y preparación; no inicia. |
| backend/runtime, model, preset, param, download | Proyecto por defecto; `--global` selecciona inventario e intención importados. |
| service/server start, stop, restart | Proceso del servicio seleccionado; no publicación de un consumidor. |
| up, down | Publicar/retirar conjunto de la receta seleccionada; no detener servicio. |
| ps, logs | Observación general o por servicio, sin cambiar intención; ps admite -w/--watch. |
| update, upgrade, verify, clean | Combinación o ámbito global explícito; no recorren YAML desconocidos. |
| tui, web, benchmark | Operaciones comunes sobre ámbito/servicio seleccionados. |

`-g` es alias de `--global` para seleccionar el ámbito global.

Un consumidor conserva inventario propio aunque su target sea global. `--global`
no equivale a seleccionar el target para cargar pesos del proyecto en la home.
No se busca una referencia en otro ámbito por coincidencia de nombre.
Un GUID de transferencia se resuelve en su ámbito; no es una cola global agregada.
Ps y el dashboard sí agregan observación de servicios conocidos, sin mutar inventarios.

| Operación | Efecto |
| --- | --- |
| up | Validar, preparar y publicar conjunto propio; preparar el global predeterminado en el primer uso y preguntar ante destino detenido. |
| up --start-service | Autorizar inicio del destino detenido; después publicar. No reiniciar activo. |
| up/down | Publicación interactiva, independiente del modo de inicio del servicio; advertencia y decisión según [ocupación](servidores.md#plazos-y-control). |
| down | Retirar conjunto publicado propio, conservando archivos, servicio y entradas ajenas. |
| service start -d | Preparar, iniciar, comprobar salud y salir sin supervisor. |
| service stop | Detener proceso verificado; afecta a todos sus consumidores. |
| service restart -d | Preparar antes de detener; iniciar y recuperar según política desacoplada. |
| update | Consultar novedades y guardar plan; pin fijo no cambia. |
| upgrade | Aplicar plan vigente; pin fijo exige cambiar intención a latest. |
| verify | Auditar y guardar resultados locales; no reparar. |
| clean | Previsualizar elegibles y borrar sólo tras confirmación o `--yes`. |

### Responsabilidades de los comandos

Hay cuatro efectos distintos: editar intención, preparar archivos, operar instancia
y observar. Los comandos informan cuál realizan. Una operación de configuración
puede ofrecer aplicar después; guardar no constituye aceptación de un reinicio.

Init preserva archivos existentes y reglas de .gitignore. No ejecuta git init.
La consulta de configuración efectiva permite revisar capas y resultado antes
de preparar. Es una responsabilidad de consulta, no un subcomando llamado como
la herramienta de referencia para composición de archivos.

### system

Consulta plataforma, arquitectura y capacidades cuando se solicita.
El diagnóstico de hardware no sustituye backend ni modifica YAML/lock para ocultar
una incompatibilidad. Las capacidades no garantizan que un modelo quepa en memoria.

### backend

Define o registra el backend del servicio en el ámbito seleccionado, su versión y variante.
Se admite el oficial con versión/variante o un ejecutable externo relativo/absoluto. Sólo el oficial admite seguimiento y update/upgrade; no hay repositorios configurables de backend. Las capacidades de forks se comprueban explícitamente.
Una combinación tiene un único backend efectivo; un override puede cambiar CUDA
por Vulkan u otra variante. Ese cambio requiere reiniciar la instancia.

La descarga explícita queda protegida localmente y no edita por sí sola YAML.
Preparar otra versión puede ocurrir con una instancia activa: se conserva su revisión.
`backend download --version <latest|bNNNN> --variant <variante>` prepara y protege
una instalación oficial. `--force` permite continuar ante incompatibilidad detectada
por el sondeo con advertencia, sin omitir integridad ni sustituir la variante.
`download resume <operation-id> --force` da esa autorización para la reanudación
de un backend; no se admite para modelos ni otras operaciones. Sin evidencia
suficiente se informa compatibilidad desconocida, según [servidores](servidores.md#backend-seleccionado-ausente).
`backend protect|unprotect <installation-id>` cambia sólo la protección explícita
de esa revisión del ámbito; no retira referencias de intención, activo o recuperación.
Un backend externo conserva sus rutas y propiedad; no se adopta por registrar.

### model

Gestiona modelos principales y companions del inventario del ámbito seleccionado.
Un alta/configuración declarativa modifica intención; una descarga explícita sólo
prepara y protege el recurso local. Esos efectos no deben confundirse.

Selectores canónicos:

```text
usuario/repositorio[:cuantización]
usuario/repositorio#tipo[:cuantización]
```

Sin cuantización, resolver candidatos en un commit e indicar etiqueta completa,
rol, archivos y tamaños. El usuario elige rutas si hay varias variantes
equivalentes por nombre. Permitir leer README y cancelar antes del GUID.
No sustituir una cuantización ausente por otra parecida ni descargar el repositorio.

Si el selector incluye una cuantización explícita y resuelve una única unidad
válida, descargarla directamente sin menú de filtrado, selección ni oferta de leer
README. Por ejemplo, `model download unsloth/gemma-4-12b-it-GGUF:Q4_0` sigue ese
camino cuando existe una sola unidad coincidente. Una unidad puede contener varios
shards; no se exige un único archivo. Si hay varias unidades de esa cuantización,
se conserva la selección entre rutas. La ausencia o resolución no concluyente se
diagnostica sin elegir otra cuantización. La descarga directa conserva las
validaciones GGUF, coherencia de shards e integridad correspondientes.

La selección de un modelo no selecciona companions vecinos. Los que estén declarados
en presets se preparan como dependencias de la combinación.
Un shard es parte de la unidad, no otro modelo.
Los [criterios GGUF](modelos-y-companions.md) gobiernan candidatos y ambigüedades.

Registrar un externo requiere rutas y nombre; --try-match es un intento informativo
opcional de correspondencia, no adopción ni certificación de procedencia.
El registro persiste aunque falle o se cancele el emparejamiento.
El nombre puede ser local: `model add ./weights/modelo.gguf --name my-model`
equivale a declarar `my-model` con esos `files` locales en `models` del YAML.
Para companions con nombre local se indica `--type mmproj|mtp`, equivalente al
campo `type` de su declaración. Un nombre canónico remoto conserva el tipo en
`#tipo`. Sin repositorio declarado, el matching se omite con diagnóstico.
Retirar externo no borra bytes; retirar gestionado actúa sólo sobre archivos propios
y conserva pendientes si la eliminación es parcial.

El archivo puede proceder de otro gestor y haber sido copiado o movido por el usuario
a la carpeta del proyecto. El alta y matching son del proyecto actual, sin importación
a home ni nueva descarga obligatoria. Registrar la copia no referencia por accidente
la ubicación original. Usar el recurso en un preset conserva esa ruta local en la
intención, relativa a la base cuando está dentro del proyecto.
Véase [reutilización local](modelos-y-companions.md#reutilizar-archivos-obtenidos-por-otra-herramienta).

Refresh relee disponibilidad/metadatos; no restablece integridad.
Verify y update no son sinónimos de refresh ni acciones de reparación.
Modelos y companions participan en update/upgrade mediante selecciones del proyecto.
`model verify <nombre>` y `model companion verify <nombre>` comprueban una unidad
del inventario del ámbito elegido. `model baseline <nombre>` y
`model companion baseline <nombre>` establecen explícitamente una referencia local
para externos; no certifican procedencia ni resuelven una discrepancia conocida.

### download

Opera por GUID estable y expone progreso por archivo/unidad.
Reanudar conserva revisión y selección; pausar conserva parciales; cancelar detiene
escritores y limpia sólo su preparación. Consultar no reanuda ni publica.
Las reglas detalladas están en [descargas](descargas.md).

Promover un recurso ya descargado a explícito no vuelve a descargarlo.
Desprotegerlo permite limpieza posterior, sin borrarlo ni declararlo íntegro.
Esa marca local no modifica lo que viaja en Git.

### param

Mantiene definiciones de argumentos del ámbito seleccionado; no exige catálogo global
obligatorio. Puede trasladarse un schema entre proyectos.
El modo permisivo admite desconocidos y valida conocidos; el estricto exige definición para todos. Ambos diagnostican
tipos, valores, ámbito, aplicabilidad y dependencias.

Args omite el prefijo -- en las claves. true emite flag; false lo omite.
Los strings "true"/"false" son valores literales. Véase
[parámetros](catalogo-de-parametros.md).
El schema puede alimentar formularios; un texto --help no es un schema verificado.

### config

Config configura Coquena globalmente en el perfil/home, sin requerir YAML.
Preferencias de observadores/reconexión y retención predeterminada de logs
pertenecen a ese ámbito. Un override de retención del servidor y el destino de sus
logs pertenecen al YAML y se editan como configuración del proyecto.
No hay selección global implícita de modelo/backend que reescriba una receta.

Ubicación de pesos/binarios, definición de servidor y schema de parámetros no son
preferencias globales de config. Restablecer definiciones de parámetros es una
edición del schema del proyecto, bajo la responsabilidad de param.
La consulta de configuración efectiva combina YAML; no consulta preferencias de
Coquena como si formaran otra capa de intención del servidor.

### preset

Mantiene presets nombrados de la intención efectiva.
Set asigna literales; bind escribe referencias `model://` o `companion://` dentro de `args`, resueltas por identidad al componer. Un nombre escrito como
literal no se transforma automáticamente en vínculo. Clone copia valores y vínculos
sin copiar archivos ni mantener herencia viva.
Un selector canónico remoto completo sin prefijo en args de un preset es una
referencia directa que evita declarar un alias. El envoltorio `literal` conserva
ese mismo texto como literal, según el [formato del proyecto](formato-proyecto.md#modelos-companions-y-referencias).

Un override puede modificar parte de un preset y desasignar un companion con valor
vacío. Retirar/desactivar un preset no borra sus pesos. Persistir no lo aplica
automáticamente al router. No confundir configuración con memoria de una instancia.

## Servidores e instancias

Cada servicio admite una instancia. Un consumidor puede publicar en un servicio
compartido; `service start` sobre activo informa que ya está ejecutándose.
Up publica el conjunto sin reiniciar. Puertos ocupados se diagnostican sin reasignar.
Preparar recursos nuevos puede ocurrir antes de detener el servidor anterior.
Un reinicio explícito valida/prepara primero, detiene y prueba lo nuevo después.
La política de recuperación depende del modo y de la última buena local;
una falla de integridad conocida es fatal en ambos modos.

### Selectores de control

Apply/withdraw opera sobre presets de la receta.
Apply publica/recarga/retira presets sobre el router compatible sin reiniciarlo
si sus capacidades lo permiten. Cambiar backend requiere reinicio.
Véase [servidores](servidores.md#aplicación-dinámica-y-memoria).

En daemon se recupera la última buena local cuando falla la candidata, con warning,
sin bucle infinito. Sin recuperación posible, se informa y termina.
En interactivo se ofrecen reintento/recuperación según disponibilidad.
La TUI ejecuta el ciclo del servicio desacoplado y presenta resultados/acciones;
no cambia esa recuperación por estar abierta. La publicación de presets conserva
su decisión interactiva independiente del modo de ejecución del proceso.

## Home

COQUENA_HOME selecciona preferencias, registro de proyectos/servicios e intención
importada con recursos globales. Los proyectos autónomos mantienen recursos propios.
La importación es explícita: consultar o consumir un servicio global no importa
pesos ni convierte su ubicación en global. Mover/copiar una carpeta no exige
identidad Git ni deduplicación automática.
En Windows, sin variable se usa $USERPROFILE/.coquena; si se define, debe ser una
ruta absoluta. Coquena no modifica variables de entorno persistentes ni autodetecta
una home junto al ejecutable. La carpeta de metadatos del proyecto y la home son
responsabilidades distintas aunque compartan un nombre convencional.

No compartir como configuración portable PID, salud local, última buena local,
credenciales, borradores ni rutas privadas de observadores.

## Gramática

```text
coquena [opciones-contexto] <comando> [argumentos] [opciones-operación]
opciones-contexto := [-f|--file <yaml>]... [--project-directory <base>]
                    [--env-file <env>]... [--private] [--global|-g]
                    [--service <nombre|default|self>]
```

Opciones de contexto se aceptan antes o después del comando, sin duplicar opciones
no repetibles. `-f` y `--env-file` conservan orden y exigen valor; `--` termina
opciones. Paths con espacios son un argumento del shell llamante. CLI no reinterpreta
su contenido como comandos. Opción desconocida o combinación incompatible es error.

`--project-directory` se resuelve contra cwd. El primer `-f` relativo se resuelve
contra esa base explícita o cwd; sin base explícita su carpeta pasa a ser base.
Los `-f` posteriores y todos los `--env-file` relativos parten de esa base. Sin `-f`,
se lee `coquena.yaml` de base; no se asciende buscando otro proyecto. `--global`
es incompatible con `-f`, env-file, private y project-directory, salvo `import`, que
recibe archivo fuente y siempre escribe global. No hay `-f -` ni entrada YAML por stdin.

El campo target de la receta sólo admite `global` y `self`, y usa `global` al
omitirse. `self` selecciona el servicio propio. El alias default del registro de
servicios identifica el global predeterminado entre los servicios de ese ámbito;
se elige con `service default set <nombre>`. Los nombres y `default` no son valores
admitidos del campo target.

`--service` selecciona servicio para control/observación y backend global. En up/apply
sobrescribe target sólo para esa operación y se registra como destino publicado,
sin editar YAML. Down sin selección retira la publicación registrada del consumidor,
incluido su destino original; una selección distinta no retira otro destino por nombre.
En service, omitir nombre y opción selecciona default; `self` exige proyecto.
El selector posicional y --service juntos son error.
Los verbos reservados start, stop, restart, remove, list, logs y default
se interpretan como gramática antes que como nombre. Para un servicio cuyo nombre
coincida con un verbo se usa --service. `service default set` es asignación de alias;
`service default logs` selecciona el alias y consulta sus logs.

```text
init [<directorio>]
import <receta.yaml> [--select <categorías>] [--as-default]
       [--replace <entidad>]... [--force]
up [--start-service]
down
service [<nombre|default|self>] start|stop|restart [-d]
service [<nombre|default|self>] remove
service default set <nombre>
service list
service [<nombre|default|self>] logs [list|follow|show <log-id>] [-f]
preset apply|withdraw <clave>
apply|withdraw <clave>
ps [-w|--watch]
logs [list|follow|show <log-id>] [-f]
update [--select <recurso>]...
upgrade [--select <recurso>]...
verify [--select <recurso>]...
verify --global [--select <recurso>]... [--include-unused]
clean [--dry-run|--yes]
combination list|forget <combination-id>
download list|show|pause|resume|cancel [<operation-id>]
backend show|set|download|list|remove|protect|unprotect
model [companion] list|show|add|set|download|refresh|remove|protect|unprotect
preset list|show|set|bind|unset|clone|remove
param list|show|define|set|unset|remove|reset
```

`server` es alias de service y `runtime` de backend: misma selección/estado.
`-d` sólo pertenece a service start/restart; stop no inicia otro proceso. Omitir
verbo en familias mutantes muestra ayuda y no realiza cambios.
`--start-service` autoriza iniciar el destino detenido sin una pregunta adicional
de inicio. Up sigue siendo interactivo para publicar; el flag no acepta riesgos
de interrupción. Up/down y apply/withdraw sin interacción disponible se rechazan
antes de publicar o retirar entradas, según [servidores](servidores.md#plazos-y-control).
Import nunca inicia, aun con force.

Las categorías de import son `service,presets,model,params`, sin repetidos ni vacíos.
Los selectores de reemplazo son `service:<nombre>`, `model:<clave>`,
`companion:<clave>`, `preset:<clave>` y `param:<clave>`. Deben existir
en la selección importada. El selector de servicio usa el name raíz; el de preset
usa la clave global promovida, sin el prefijo del proyecto. Modelos y companions
locales también pierden ese prefijo al importar; las identidades remotas se conservan.
Los parámetros mantienen sus claves.
`--force` no se combina con select/replace y no elimina entidades ausentes.

Selectores de recursos para update/upgrade y verify: referencias
`backend://<variante>`, `backend://<alias>`, `backend://<proyecto>/<alias>`,
`model://<clave>` y `companion://<clave>`, repetibles como unión.
El ámbito lo define el contexto de proyecto o `--global`; calificar un alias no
cambia el ámbito de búsqueda. Alias, calificación y ambigüedad se definen en
[servicios y recetas](servicios-recetas.md#nombres-y-destino).
Por ejemplo, `--select backend://cuda@12` selecciona esa variante en el ámbito elegido;
puede corresponder a más de un servicio global. Para modelos y companions también
se admite la identidad remota completa: `model://unsloth/LFM2.5-8B-A1B-GGUF:UD-IQ1_M`
o `companion://autor/repositorio#mmproj:BF16`. El tipo debe coincidir con el esquema
y la identidad remota debe incluir cuantización. Las claves locales se resuelven
dentro del ámbito, sin buscar recursos de otros proyectos ni del target global.
Estas referencias seleccionan recursos existentes en la combinación o catálogo;
no son solicitudes de descarga ni cambian la selección declarada.
Sin select se usa la combinación seleccionada; con `--global`, las intenciones
registradas del catálogo global. Un selector desconocido no amplía al conjunto completo.
Un plan parcial nuevo no autoriza aplicar propuestas excluidas. `combination forget`
requiere ID completo del ámbito y no olvida una instancia activa ni retira presets.

En proyecto, verify comprueba la receta completa de la combinación elegida y los
recursos que utiliza; `--select` permite limitar esos recursos. No admite
`--include-unused`. No audita el
backend de un servicio target global ni los recursos de otros proyectos.
Con `--global`, comprueba las definiciones y recursos usados del catálogo global;
`--select` limita la comprobación a recursos de ese catálogo y permite elegir uno
instalado sin uso. `--include-unused` agrega las instalaciones del inventario global
que no estén utilizadas. Sólo `--include-unused` es exclusivo del ámbito global.

Set recibe entidad, argumento y valor YAML escalar/lista como un argumento de CLI:
`preset set charla ctx-size 8192`, `preset set charla texto '"8192"'`.
Bind recibe entidad, argumento y referencia tipada; unset escribe null en la capa
editable para retirar herencia. Clone recibe origen y nombre nuevo.
Backend set acepta `--version <latest|bNNNN> --variant <variante>` o
`--executable <ruta>`, excluyentes. Model/companion add recibe `--name <clave>` y
`--file <ruta>` repetible; `--try-match` es optativo. Download recibe selector remoto
o backend version/variant y protege el resultado sin editar YAML.
Define recibe nombre y `--file <definición.json>`; reset muestra impacto y exige
confirmación o --yes, sin descartar valores de presets. Remove nunca implica
cascada de archivos ajenos. Opciones de campos específicos siguen sus canónicos.

Clean sin opciones previsualiza y pregunta; sin interacción sólo previsualiza y
diagnostica falta de confirmación. Dry-run nunca borra; yes autoriza el conjunto
mostrado sólo si su revisión sigue vigente. No existe clean --force.
Logs sin subcomando lee salida reciente; -f equivale a follow y no es --file en
esa familia. `logs show <id>` selecciona un histórico y no se combina con follow.
IDs de log son locales al servicio, nunca rutas arbitrarias inferidas.
Ps sin servicio muestra los servicios conocidos; con selección limita la consulta.
`ps -w` y `ps --watch` activan la observación continua; watch es una opción, no
un subcomando. Sin esa opción ps muestra una instantánea y termina.
Con salida redirigida, ps --watch emite snapshots separados por newline, sin
escapes de terminal.

La consulta de configuración efectiva redacta expansiones sensibles; no descarga ni crea inventario
ni inicia. Las consultas list/show no se vuelven transferencias por mostrar progreso.
No hay pregunta obligatoria al cargar por warmup ni al observar.

La selección de una receta ya registrada consulta su referencia y nombre de receta en
`.coquena/local/` antes de derivar nombres desde el YAML actual o la carpeta. Cambiar esos
datos no ejecuta bajas, renombrados o altas implícitos. Up y down usan nombres
registrados y muestran diferencias con la intención. Service remove da de baja
un servicio detenido sin eliminar recursos; rechaza identidad incierta, instancia
activa o contribuciones que todavía lo referencian. Si era default, retira el alias
sin elegir sustituto. Sólo después de la baja confirmada de las entidades afectadas
puede una nueva alta adoptar los nombres actuales. Force de import no es un rename.
Para el conjunto de presets, esa baja y alta son down y up: down usa los nombres
anteriores registrados; tras retirada confirmada, up publica con los actuales.
Down/up opera sobre presets. Los comandos de modelos del router no intervienen
en ese cambio de nombres.

## Códigos de salida y diagnósticos

| Código | Resultado |
| --- | --- |
| 0 | Operación completa o sin cambios; warning de recuperación si quedó disponible. |
| 1 | Error interno no clasificado, con diagnóstico local. |
| 2 | Sintaxis CLI, YAML/schema, referencia o selección inválida. |
| 3 | Conflicto de revisión/nombre, dependencia en uso o confirmación requerida. |
| 4 | Consulta remota/autenticación/red no completada. |
| 5 | Capacidad/backend/hardware incompatible o no acreditado para la operación. |
| 6 | Integridad inválida o evidencia insuficiente para certificar verify. |
| 7 | Transferencia, extracción, lectura/escritura o publicación física fallida. |
| 8 | Ejecución/salud/carga fallida, o servicio detenido sin autorización de inicio. |
| 9 | Mutación remota de resultado no confirmado; consultar antes de actuar de nuevo. |
| 130 | Cancelación del usuario; conservar lo recuperable. |

Resultado incierto domina sobre éxito local; no se convierte timeout en cancelación
remota. En operaciones con varios diagnósticos se conserva cada resultado y el
código del primer fallo por fase: validación, coordinación, preparación, ejecución
o publicación; incertidumbre posterior usa 9. Un fallo de warmup produce 8 aunque
la publicación se haya confirmado. Estado simplemente detenido en ps no es error.
Datos normales van a stdout, diagnósticos a stderr; secretos se redactan en ambos.
El diagnóstico contiene código estable de área, ubicación, causa y efecto observado,
sin depender de analizar texto traducido para decidir la siguiente acción.
