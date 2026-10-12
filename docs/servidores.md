# Servidores e instancias

La modalidad declarativa de [M3](milestones/M3-definiciones-declarativas.md)
se describe en [su documento funcional](definiciones-declarativas.md). Las reglas
siguientes describen la gestión persistida por CLI.

## Definición persistida

Un servidor reúne una lista explícita de presets, una selección de backend y parámetros
del router. Se crea y modifica incrementalmente por CLI; cada modificación se persiste.
Crear no inicia. Los cambios afectan al próximo inicio, no a procesos ya ejecutándose.
Agregar un preset al catálogo no lo incorpora a ningún servidor; puede ofrecerse en varios.

Coquena permite designar un servidor predeterminado. No es una entidad especial ni exige
el nombre `default`. Sin selección propia de backend, el servidor hereda el predeterminado
de Coquena al iniciar; cambiar ese predeterminado afecta sólo a futuros inicios.

Para el inicio rápido, Coquena parte de un servidor llamado `default`, designado como
predeterminado y sin selección propia de backend. Puede modificarse o designarse otro
servidor como predeterminado. Su lista de presets sigue siendo explícita: esta creación
inicial no publica automáticamente presets del catálogo.
El primer backend descargado e instalado correctamente por Coquena se autoasigna como
backend predeterminado, conservando la selección latest o el pin solicitado. Un registro
externo no activa esta asignación; descargar bytes sin completar la instalación tampoco.
No se sustituye una selección predeterminada ya establecida por el usuario.
Repetir `backend install` fuerza una descarga nueva y reinstalación, incluso si el
build ya está instalado. Con `latest` vuelve a resolver el filtro y, si encuentra un
build nuevo, lo instala y lo designa como actual de ese seguimiento. No requiere
`update` previo: ésta es una excepción explícita al recorrido update/upgrade.
Con release explícito reinstala ese build y conserva el pin. Reinstalar conserva
el ID de un build ya registrado, no convierte un default fijado en latest ni viceversa.
La instalación anterior sigue disponible ante un fallo; se rechaza reemplazar un
backend que esté usado por una instancia activa o cuya identidad no pueda resolverse.

Instalaciones posteriores no cambian silenciosamente ese predeterminado. La ausencia
por retirada posterior no autoriza a reemplazarlo automáticamente por otra instalación.

Una selección puede seguir `latest` dentro de su filtro (por ejemplo CUDA 12) o fijar
un release. Se conserva la intención además del build instalado. `backend update`
consulta novedades; `backend upgrade` instala y cambia el build actual de las selecciones
que siguen `latest`, sin sustituir releases fijados. Iniciar no consulta ni descarga
actualizaciones: resuelve el build actual instalado. Una instancia conserva el build
con el que arrancó. Un upgrade fallido no sustituye al anterior.
Sin selector, backend update/upgrade recorren todos los seguimientos latest registrados;
con selector, sólo el indicado. Update guarda el build detectado y upgrade aplica ese
build, sin resolver silenciosamente una novedad posterior. Releases fijados y externos
no participan como selecciones a actualizar. Un pin fija un build específico;
no fija ni desactiva el seguimiento `latest`. Si ese build es también el actual de
un seguimiento `latest`, update consulta ese seguimiento y upgrade puede avanzar
su build actual, conservando la selección fijada y su instalación protegida.
Un fallo de una unidad no impide tratar las demás.

Si el candidato ya está instalado como backend gestionado del mismo filtro,
upgrade verifica el árbol de instalación, su identidad y `llama-server --version`,
y reutiliza ese registro para avanzar latest. Conserva su ID, pin y archivos,
incluso si una instancia lo está usando. Una validación fallida conserva el
seguimiento anterior y se diagnostica; no fuerza una reinstalación implícita.
Sin observación aplicable del build actual, upgrade solicita ejecutar update.
Una operación pendiente se continúa por su GUID con `download resume` o se cancela;
otro upgrade del mismo filtro no crea una descarga duplicada.

Cada consulta guarda su fecha, filtro, build instalado de partida y diagnóstico.
Una novedad conserva un candidato concreto con sus artefactos y evidencia de
compatibilidad. Sin build posterior se informa «sin cambios», sin proponer un
downgrade. Una consulta fallida se informa como tal y deja sin candidato aplicable
ese seguimiento; no equivale a estar actualizado. Si la instalación o una consulta
concurrente cambian durante el sondeo, se rechaza guardar un resultado obsoleto.
`backend list` muestra la última observación guardada que corresponde al build
actual, sin consultar la red.

Los upgrades conservan los binarios anteriores con un límite configurable: cinco builds
por distribución, familia y plataforma, contando el actual. Tras un upgrade exitoso,
la limpieza retira primero los más antiguos, excepto el actual, los fijados explícitamente
y los usados por instancias activas. Si los protegidos superan el límite, se conservan
y se informa; no se rompe una selección o instancia para cumplirlo. Los externos nunca
se borran físicamente mediante esta política.

Las variantes de una misma familia comparten el límite: un build presente en
CUDA 12 y CUDA 13 cuenta una vez. Si alguna instalación de ese build está protegida,
se conserva el build. También se protegen referencias fijadas de servidores,
identidades de procesos inciertas y recursos de operaciones pendientes.
La retirada guarda una cola persistente antes de quitar el registro del catálogo;
si falla el borrado físico, el build actual permanece publicado y la limpieza
pendiente se reintenta tras otro upgrade exitoso. Una descarga cuyo upgrade ya se
publicó conserva su GUID hasta completar la limpieza; reanudarlo no vuelve a descargar.

`config backend-retention show` consulta el límite efectivo y su origen (inicial o
propio); `set <cantidad>` guarda un entero positivo y `unset` recupera los cinco builds
iniciales. La política se guarda en `config.json` como `backendRetention.maxBuilds`.
`backendRetention.own` distingue un ajuste propio incluso cuando su valor es cinco;
un documento anterior sin esa marca conserva el origen inicial para cinco y propio
para otro valor.
Consultar o modificar el límite no borra instalaciones: se aplica tras el próximo
upgrade exitoso, conservando las protecciones indicadas. Valores inválidos o fallos
de persistencia no dejan un ajuste parcial.

La sintaxis de defaults, selección de backend y edición incremental está en
[comandos.md](comandos.md#servidores-e-instancias). Backend use establece la selección
global; server use default recupera su herencia. Ambos configuran, no descargan ni inician.

## Identificación de backends

El selector completo es `origen/variante:release` (o `latest`). El origen es un nombre
corto asociado inequívocamente a un repositorio concreto. `ggml` es el nombre canónico
del origen oficial `ggml-org/llama.cpp`; `oficial` es su alias y se normaliza a `ggml`.
El repositorio real sigue identificando la distribución para el catálogo de parámetros.

Ejemplos: `oficial/cuda@12:latest`, `ggml/cuda@13:latest`, `lab/especulative:b1234`.
Una variante modificada puede tener nombre propio; su familia de aceleración se guarda
aparte cuando el nombre no la expresa. `especulative` es sólo un ejemplo, no una familia
de hardware. El selector abreviado `familia:release` conserva el origen oficial implícito.

Los externos requieren un nombre específico único, case-insensitive, para seleccionarlos
directamente, por ejemplo `external-binary`. Apunta a una instalación concreta, sin
seguimiento automático. No puede ser `default` ni contener `/` o `:`, para distinguirlo
de los selectores. Se conserva el ID interno estable; no se exige `--backend`.
Externo indica propiedad de archivos, no que la distribución sea necesariamente no oficial.
Los externos se reconocen en el catálogo, pero no se gestionan: se seleccionan sólo
por su nombre propio y no participan en los selectores de backends gestionados.
`backend update` y `backend upgrade` no siguen ni actualizan externos. Su distribución
declarada sirve para asociar parámetros, no para incluirlos en la resolución de `latest`.

Sólo el origen oficial permite descargas en esta versión. Otros nombres de origen
identifican binarios obtenidos y registrados por el usuario, no habilitan descargas,
consulta de releases ni confianza en terceros. `latest` no amplía ese alcance.
Los externos se registran con `--name <nombre>` obligatorio, `--family <familia>` y
`--distribution <repositorio>` opcional; omitir la distribución conserva el origen
oficial implícito. El nombre no certifica procedencia ni sustituye el sondeo válido
de `llama-server --version`. Los comandos para declarar nombres cortos de otros orígenes
y variantes quedan aplazados: en esta versión basta --distribution con el repositorio
y el nombre propio del externo. Registrar un externo del mismo origen/familia/release
que uno gestionado no crea una colisión de selección: pertenecen a formas de acceso distintas.

## Parámetros del servidor

El catálogo distingue el ámbito servidor/router del ámbito modelo/inferencia y el
destino de emisión (CLI o INI). Los valores del servidor no se mezclan automáticamente
con los valores de los presets. Dos servidores pueden usar el mismo backend con
parámetros distintos; los valores pertenecen al servidor, no al binario ni a su familia.

`host`, `port` y `metrics` son definiciones estáticas de Coquena, independientes del
binario seleccionado y no eliminables del catálogo. Sus defaults propios son:

| Parámetro | Default |
|---|---|
| host | 127.0.0.1 |
| port | 9931 |
| metrics | true |

Cada servidor puede sobrescribirlos. El puerto sigue además las reglas de asignación
y unicidad de la sección Puertos. Unset elimina el valor propio de host o metrics y
recupera su default; para port solicita la reasignación descrita abajo. No elimina
la definición. Otros parámetros del servidor se agregan mediante el catálogo,
con sus reglas de distribución y release. La presencia de una definición no garantiza
que un binario personalizado la admita; no se descarta silenciosamente.

## Puertos

Cada servidor registrado tiene un puerto distinto. Al crear, --port permite elegirlo
explícitamente; se valida su rango y que no pertenezca a otro servidor del catálogo.
Sin --port, se asigna el mayor puerto registrado más uno; con el catálogo vacío se
usa 9931, también al crear el default inicial. Coquena informa el puerto asignado.
El registro guarda ese valor concreto. Los puertos ya persistidos se conservan;
start y restart emiten explícitamente --port sin migrarlo ni reasignarlo.
Si se alcanza el máximo 65535 y no puede sumarse uno, se rechaza la creación sin
crear el registro: no se vuelve al principio ni se buscan huecos, pues puede haber
puertos reservados. El usuario puede elegir explícitamente otro puerto válido.
Modificar un puerto también respeta la unicidad entre servidores registrados.
Server unset port aplica la misma asignación automática: mayor puerto guardado
en el catálogo más uno, informando el nuevo valor. No rechaza por una colisión
con 9931 ni vuelve a ese default. Si el máximo guardado es 65535 (2^16 - 1),
rechaza la acción y conserva el puerto anterior; no busca huecos ni reinicia
la numeración. El cálculo y el guardado deben conservar la unicidad sin perder
el valor previo si la acción falla.

La asignación automática ocurre al crear, no al iniciar. Si el puerto está ocupado
por otro proceso al iniciar, se rechaza con diagnóstico, sin cambiar la configuración
ni elegir otro. La consulta del catálogo no garantiza disponibilidad en el sistema.

## Backend seleccionado ausente

`backend uninstall` se rechaza sólo si alguna instancia activa utiliza ese backend
concreto, indicando los IDs que deben detenerse. Que una definición de servidor lo
seleccione no impide retirarlo si no hay instancias que lo estén ejecutando.
En ese caso se permite retirar el backend y se conserva el servidor huérfano, con
diagnóstico de backend ausente. No confundirlo con un proceso huérfano.
La protección de builds fijados en la limpieza automática no impide esta eliminación
explícita cuando el backend no está en ejecución. Retirar un externo elimina sólo
su registro; retirar un gestionado elimina sus archivos conforme a las reglas de propiedad.

Si se elimina el backend seleccionado, se conserva la configuración del servidor.
El próximo inicio se rechaza con un diagnóstico; no se sustituye silenciosamente
por otro backend. Esto también aplica si la selección heredada ya no resuelve a
una instalación disponible. Una selección latest sólo utiliza el build actual
instalado de su filtro: iniciar no descarga una sustitución ni cambia de familia.

Los parámetros se modifican sólo en la definición persistida mediante set/unset.
Start no admite sobrescrituras temporales de backend ni parámetros: show refleja
la configuración que se iniciará. La opción -d selecciona el modo de ejecución,
no modifica la definición del servidor.

## Inicio y detención

Retirar un modelo o companion se rechaza mientras alguna instancia activa lo utiliza,
indicando sus IDs. Las referencias de presets o servidores detenidos no impiden retirarlo:
se conservan sus configuraciones con referencias rotas y diagnóstico; el próximo inicio
se rechaza hasta corregirlas. Esto no autoriza borrar archivos externos: se retira sólo
su registro. El retiro gestionado respeta la propiedad y la unidad completa de shards.
Update de modelos/companions puede consultar novedades en uso, pero upgrade se rechaza
antes de descargar mientras alguna instancia utilice la unidad, incluidos renombres.
No se mantienen revisiones de pesos para servir simultáneamente; el área temporal de
preparación y la conservación del anterior ante fallos siguen siendo necesarias.

Se puede guardar un servidor vacío, pero no iniciarlo. Si algún preset tiene referencias
rotas, archivos faltantes, inaccesibles o inválidos, se rechaza todo el inicio con diagnóstico;
no se omiten presets. Coquena no comprueba compatibilidad de inferencia: la decide el backend.

Cada servidor admite como máximo una instancia activa. Un segundo start informa
que ya está ejecutándose y no inicia otro proceso. Distintos servidores sí pueden
ejecutarse simultáneamente con sus puertos distintos. Los controles de inicio deben
evitar duplicados incluso entre invocaciones concurrentes.

Cada inicio genera un INI propio en el home de Coquena y una instancia con ID, backend/build
concreto, dirección de escucha, PID y log. En primer plano muestra salida; Ctrl+C detiene
esa instancia. Con `-d` devuelve el prompt y el proceso sobrevive al cierre de la terminal.
Stop detiene la única instancia del servidor indicado; sin nombre actúa sobre el
servidor predeterminado. `server stop --id <id-instancia>` permite seleccionar una
ejecución concreta. No elimina la definición ni detiene otras instancias.
Si el servidor existe y ya está detenido, informa que no hay instancia activa y
termina correctamente. Un nombre/ID ausente, default no designado o identidad
incierta se diagnostican; stop no crea, recrea ni designa servidores.

Stop, restart y Ctrl+C solicitan una parada ordenada y esperan hasta diez segundos.
Si la instancia de identidad verificada no termina, se fuerza el cierre de su grupo
de procesos y se informa. En primer plano se muestra el label de control; el primer
Ctrl+C confirma su recepción e indica la espera y la posibilidad de forzar. Un segundo
Ctrl+C fuerza el cierre inmediatamente, sin esperar el plazo. No se limpia el registro
ni el INI hasta confirmar la terminación; el log se conserva también al forzar.

`server [nombre] restart` detiene y vuelve a iniciar utilizando la definición guardada
y resolviendo nuevamente su selección de backend. Si sigue `latest` y se hizo un
`backend upgrade` exitoso, utiliza el nuevo binario descargado, no el build de la
instancia anterior. Un release fijado o un externo conserva su selección concreta.
Restart no consulta ni descarga novedades por sí mismo. Si el servidor está activo,
detiene su única instancia y vuelve a iniciarlo; si está detenido, lo inicia. La nueva
instancia se ejecuta en segundo plano y el comando devuelve el prompt sin seguir logs,
aunque la ejecución anterior fuese en primer plano. Se informa el ID de la nueva instancia.
La analogía con docker compose restart se limita al ciclo y a no adjuntar salida:
Coquena sí aplica la configuración guardada actual y el build actualizado de latest.
Se aplican las mismas validaciones de inicio; no se lanza un sustituto si fallan.

Server list consulta definiciones guardadas. Server [nombre] show muestra la selección
propia o heredada, build instalado resuelto, parámetros efectivos y presets asociados,
sin iniciar procesos ni consultar novedades remotas. Server ps muestra una instantánea de instancias
activas y sus métricas; termina sin monitoreo continuo. Una consulta fallida o métricas
desactivadas no ocultan la instancia. En Router Mode las métricas se consultan por modelo,
sin mezclar tasas entre modelos. La futura TUI podrá aprovecharlas, sin adelantar su alcance.

## Consulta de logs

`server [nombre] logs` muestra salida reciente; con `-f` sigue la salida. Ctrl+C
interrumpe sólo la consulta, no detiene la instancia. Sin nombre consulta el servidor
predeterminado. Se consulta el log de la ejecución actual o, si el servidor está detenido,
el de su última ejecución. La retención tiene un límite de tamaño configurable; no se
acumula un historial ilimitado. El límite inicial es 100 MiB por servidor; detener no elimina el último log disponible.

La política global se guarda como `logRetention: { "maxBytes": 104857600 }` en
`config.json`; omitirla usa ese valor inicial. El límite propio se guarda como
`logRetention` en la definición del servidor, separado de los parámetros de llama-server.
Omitirlo recupera la política global. `config log-retention` y `server log-retention`
permiten consultar, ajustar y restablecer estos valores.

Cada servidor puede guardar un límite propio; si no lo tiene, hereda el global.
El límite global se aplica a cada servidor, no a la suma de logs de todos los servidores.
Restablecer la política global recupera los 100 MiB iniciales; restablecer la de un
servidor elimina su override y recupera la herencia global.

Consultar la política muestra el límite efectivo, los bytes exactos, una representación
legible con unidad binaria y su origen: inicial, global o propio. Cambiar o restablecer
el límite no elimina archivos inmediatamente; la próxima limpieza usa el nuevo valor.

El tamaño admite bytes (`B` o sin sufijo), unidades decimales y unidades binarias:

| Unidad | Bytes |
|---|---:|
| B | 1 |
| KB | 1 000 |
| MB | 1 000 000 |
| GB | 1 000 000 000 |
| KiB | 1 024 |
| MiB | 1 048 576 |
| GiB | 1 073 741 824 |

`100MB` equivale a 100 000 000 bytes; `100MiB`, a 104 857 600 bytes.
Se admiten fracciones con punto decimal, unidades sin distinguir mayúsculas y
espacios entre cantidad y unidad: `1.5GiB`, `100mib` y `100 MiB` son válidos.
El tamaño convertido debe equivaler exactamente a un número entero de bytes;
no se redondea ni trunca. `0.5KiB` equivale a 512 bytes; `0.1KiB` se rechaza.
Si contiene espacios, el tamaño se pasa como un único argumento entre comillas:
`"100 MiB"`. La coma decimal y los separadores de miles no se admiten.
La unidad sólo expresa la entrada: la política persiste el entero `maxBytes`, sin
cambiar el formato existente. Un tamaño inválido, no positivo o que exceda la
capacidad de `maxBytes` se rechaza antes de modificar la configuración. Su rango es
de 1 a 9 223 372 036 854 775 807 bytes (entero de 64 bits con signo).

La limpieza ocurre al iniciar, detener o consultar logs, bajo exclusión de escritura.
Se retiran primero archivos de ejecuciones antiguas; se protege el log activo y el
último, incluso cuando exceden el límite. Se informa el exceso sin truncarlos.
No hay rotación continua: un log protegido puede superar el límite incluso después
de una limpieza. El límite restringe el historial eliminable, no garantiza una cuota
estricta de disco. Logs muestra hasta los últimos 64 KiB, omitiendo la primera línea parcial; -f continúa sobre ese mismo archivo,
sin adjuntarse a una ejecución posterior tras restart.

`run/<servidor>/last-log.json` conserva schemaVersion, serverId, instanceId y logPath.
Se publica antes de activar el proceso y antes de limpiar el registro activo.
Su reemplazo es atómico y la referencia se valida contra el servidor y la ruta gestionada.
Los logs antiguos sin referencia (anteriores a 013) no se eligen por adivinación;
una instancia activa anterior sí establece la referencia al consultar logs o detener.

Ps verifica identidad y conserva diagnósticos de incertidumbre. Consulta el estado
por GET /models y las métricas sólo de modelos cargados, por GET /metrics con model
y autoload=false. Usa nombres de presets del INI de la ejecución, no del catálogo
modificado después del inicio. No provoca cargas ni mezcla tasas. La consulta tiene
un plazo de cinco segundos por instancia y respuestas limitadas a 1 MiB; errores,
modelo no cargado o métricas desactivadas no producen cifras sustitutas ni eliminan
el registro activo. Se muestran tasas de prompt/generación y contadores reconocidos
que realmente estén presentes, con sus nombres Prometheus.

Referencia técnica: [API del router llama.cpp b11482](https://github.com/ggml-org/llama.cpp/blob/b11482/tools/server/README.md#routing-requests).

## Retirar un servidor

Se permite retirar cualquier servidor sólo cuando esté detenido. Se elimina su definición,
no los presets, modelos, companions ni backend asociados. Si existe una instancia activa,
se rechaza la acción indicando su ID; primero debe detenerse.
El comando es `server [nombre] remove`; sin nombre retira el servidor predeterminado.
`server default remove` retira específicamente el servidor llamado default; la lista
de acciones reservadas distingue esa referencia de `server default <nombre>`, que
designa el predeterminado. La gramática se define en comandos.md.
Si se retira el predeterminado, se limpia esa designación sin elegir otro ni recrear
`default` automáticamente. Los comandos que omiten nombre informan que debe designarse
un servidor existente como predeterminado. La creación inicial de default es de primera
configuración, no una reparación automática de una eliminación solicitada.
El usuario sí puede crearlo nuevamente con `server create default` cuando el nombre
esté libre, y designarlo con `server default default` una vez existente. Crear y designar
son acciones distintas; la interpretación posicional evita confundir esos nombres con
subcomandos. Esto no cambia la prohibición de recreación automática.

## Directorios gestionados

Coquena permite consultar y ajustar por separado los directorios de backends y de
modelos mediante `config backends-dir` y `config models-dir`. El directorio de modelos
incluye los companions gestionados. Estos ajustes no cambian el home del manager ni
autorizan mover o adoptar archivos registrados como externos.

Los ajustes admiten rutas absolutas y relativas. Una ruta relativa se conserva como
tal en la configuración, sin convertirla permanentemente a absoluta. Esto permite
reubicar el conjunto de directorios conservando su estructura relativa. La ruta
absoluta efectiva se resuelve al utilizar el ajuste, tomando siempre el home de Coquena
como base, independientemente del directorio desde el que se invoque la CLI. Por ejemplo,
`../Modelos` con home `D:/IA/Coquena` resuelve a `D:/IA/Modelos`; trasladar el conjunto
a `E:/IA` y actualizar el home conserva esa relación.

Los registros de recursos gestionados conservan su ubicación de instalación y las
rutas de sus archivos relativas a ella. La ubicación, cuando es relativa, se resuelve
respecto al home. No se reemplaza una ubicación anterior por el ajuste vigente si el
usuario eligió no trasladar: cada recurso conserva la base donde realmente se instaló.
Así pueden coexistir ubicaciones antiguas y nuevas y reubicarse junto con el home si
su relación es relativa. Las ubicaciones absolutas permanecen fijadas a su ruta.
Las rutas absolutas para ejecutar binarios o generar el INI se resuelven al utilizarlas.

El destino debe ser un directorio utilizable. Si no existe, Coquena lo crea al aplicar
el cambio; si la ruta apunta a un archivo o no permite las operaciones necesarias,
rechaza el ajuste. La validación se realiza antes de mover recursos o persistir la
nueva ubicación. Un rechazo no modifica el ajuste ni los archivos existentes.
El directorio de backends no puede invadir el home completo ni sus áreas operativas
de catálogos, descargas, ejecución, logs o modelos.

Las ubicaciones predeterminadas son `<home>/backends` y `<home>/models`. Show informa
la ruta efectiva y si procede del default o de un ajuste propio. Unset elimina el ajuste
y recupera la ruta predeterminada; ofrece el mismo traslado y aplica las mismas reglas
de bloqueo, fusión y confirmación que set. El home se resuelve conforme a la
[CLI](comandos.md#home).

Al cambiar un directorio, Coquena pregunta si se trasladan los archivos gestionados
existentes. Antes de elegir muestra los recursos afectados y las ubicaciones de origen
y destino. Si se acepta el traslado, mueve los recursos y actualiza sus rutas conservando
identidades y referencias. Para modelos, una unidad con shards se trata completa.
Para backends, el traslado reúne todas las instalaciones gestionadas, aunque estén
en distintas ubicaciones: se traslada el directorio completo de cada instalación,
incluidos DLL, subdirectorios y archivos auxiliares. Las referencias externas y los
archivos ajenos que estén fuera de esos directorios conservan su ubicación.

El origen se conserva hasta verificar la copia completa y publicar conjuntamente
el catálogo y la configuración. Una interrupción conserva el estado de recuperación;
el siguiente comando de escritura reintenta la copia o completa el checkpoint y la
limpieza según el punto alcanzado. Antes de publicar, los lectores conservan las
rutas anteriores; después, observan las nuevas rutas conjuntamente. Un fallo posterior
a la publicación informa que la ubicación está publicada y la limpieza sigue pendiente.

Si se elige no trasladar, conserva los archivos y sus rutas registradas; la nueva ubicación
se usa para futuras instalaciones. El catálogo puede contener recursos gestionados en
distintas ubicaciones y los resuelve por sus rutas guardadas, sin perder su propiedad.
Cancelar o no disponer de entrada interactiva deja el ajuste y los archivos sin cambios.

Si se elige trasladar y algún recurso afectado está en uso o tiene una descarga
pendiente, Coquena bloquea el traslado completo antes de mover archivos o cambiar
el directorio. Muestra los recursos, instancias y operaciones que impiden continuar,
para que el usuario detenga las instancias o finalice/cancele las descargas primero.
No detiene procesos ni cancela descargas automáticamente y no realiza un traslado
parcial de los recursos disponibles.

El traslado fusiona por defecto los archivos con el contenido del directorio destino.
Antes de comenzar, Coquena analiza todos los archivos y reporta las colisiones, con
las rutas de origen y destino que se sobrescribirían. Los archivos del destino sin
colisión se conservan. Si hay colisiones, advierte que la fusión destruirá el contenido
anterior de esos archivos y solicita una única confirmación para todo el traslado.
Aceptar autoriza todas las sobrescrituras informadas; cancelar o no obtener respuesta
deja archivos, rutas registradas y ajuste del directorio sin cambios. No se realizan
sobrescrituras ni traslados antes de completar el análisis y obtener esa confirmación.

La comprobación de uso incluye los recursos afectados en el destino: la confirmación
no habilita sobrescribir archivos utilizados por instancias o descargas. Los archivos
registrados como externos mantienen su protección. Una colisión que aparezca después
del informe no queda autorizada por la confirmación anterior.

Se recomienda elegir los directorios antes de descargar recursos o preparar servidores.
Cambiar después está permitido, con la elección de traslado y la advertencia explícita
del impacto destructivo de la fusión cuando existan colisiones.

## Persistencia

El formato sigue la [regla de persistencia](implementacion-catalogo-modelos.md#almacenamiento-local-acordado):
JSON indentado para estado y configuración, JSONL para el log de transacciones y texto
para la salida original del backend. JSONL no define un historial reproducible del servidor.
Las definiciones se guardan en `<home>/catalogs/servers.json`; las selecciones
predeterminadas y políticas, en `<home>/config.json`. Bajo `<home>/run/<servidor>/`
se guardan el registro JSON de instancia y su INI; los logs de texto van bajo
`<home>/logs/<servidor>/`. La definición guarda la selección propia de backend y el
límite propio de logs, si existen; sus valores predeterminados se resuelven al consultar
o iniciar. La instancia registra el backend/build y la escucha efectivos. El esquema exacto y la recuperación requieren pruebas.

### Estructura mínima de JSON acordada

La estructura mínima y el significado de los campos siguientes están acordados; no
implican iniciar la implementación. Los IDs abreviados son placeholders; su representación,
validación completa se concreta junto con los otros catálogos.
SchemaVersion versiona el documento de persistencia, no llama.cpp.

Fragmento de config.json (no reemplaza el resto de su configuración):

```json
{
  "schemaVersion": 1,
  "defaultServerId": "server-default-id",
  "defaultBackend": {
    "kind": "latest",
    "origin": "ggml",
    "variant": "cuda@12"
  },
  "backendRetention": {
    "maxBuilds": 5
  },
  "backendsDirectory": "../Backends",
  "logRetention": {
    "maxBytes": 104857600
  }
}
```

Servers.json guarda valores propios, el puerto asignado y el límite propio de logs si existe.
BackendsDirectory se omite para utilizar el directorio predeterminado. En el catálogo
de backends, installationDirectory conserva la ubicación registrada y serverPath es
relativa a ella. Los registros anteriores sin installationDirectory conservan su
serverPath relativa al home; no se reinterpretan con el ajuste vigente.
Backend null representa herencia del predeterminado, no selección automática entre todos los binarios.
Una selección concreta usa backendId, tanto para un release fijado como para un externo;
la modalidad latest guarda origen/variante. Los nombres actuales se resuelven al mostrar.

```json
{
  "schemaVersion": 1,
  "servers": [
    {
      "id": "server-default-id",
      "name": "default",
      "backend": null,
      "presetIds": [],
      "parameters": {
        "port": 9931
      }
    },
    {
      "id": "server-lts-id",
      "name": "lts",
      "backend": {
        "kind": "fixed",
        "backendId": "backend-b10201-id"
      },
      "presetIds": ["preset-chat-id"],
      "logRetention": {
        "maxBytes": 524288000
      },
      "parameters": {
        "port": 9932,
        "metrics": false
      }
    }
  ]
}
```

Host y metrics omitidos usan los defaults de Coquena. PresetIds vacío permite guardar,
no iniciar. Un ID ausente conserva la referencia rota, sin enlazar a otra entidad con
igual nombre. No se copian rutas de modelos ni valores de presets en servers.json.

Registro de una ejecución bajo `run/lts/instance.json`:

```json
{
  "schemaVersion": 1,
  "instanceId": "instance-id",
  "serverId": "server-lts-id",
  "backendId": "backend-b10201-id",
  "build": "b10201",
  "pid": 12345,
  "processCreated": 134043912000000000,
  "executablePath": "D:\\Coquena\\backends\\b10201\\llama-server.exe",
  "mode": "detached",
  "host": "127.0.0.1",
  "port": 9932,
  "metrics": false,
  "presetIds": ["preset-chat-id"],
  "resourceIds": ["model-id", "companion-id"],
  "iniPath": "run/lts/instance-id.ini",
  "logPath": "logs/lts/instance-id.log"
}
```

Las rutas de INI y log del ejemplo son relativas al home. ExecutablePath registra
la ruta absoluta del proceso observado en Windows.
ProcessCreated guarda su fecha de creación FILETIME; ambos campos, el PID y la pertenencia
al job de esa instancia se verifican antes de reconocerla o enviar señales.
Un registro anterior sin esos campos conserva identidad incierta; no se completa por PID.
La ejecución registra recursos efectivos
para protegerlos aunque luego cambie la definición del servidor o preset. Un PID guardado
no prueba que el proceso siga vivo ni que sea el mismo; el mecanismo de identidad y
recuperación debe verificarse al implementar. Inicio y escritura se coordinan para no
confundir una ejecución real con una publicación parcial del registro.

La política de logs se concreta en la sección Consulta de logs; la referencia al último log no es una instancia activa.

## Recuperación tras un cierre inesperado

No se reinician servidores automáticamente. Al consultar o actuar sobre registros de
instancias, se verifica si el proceso existe y pertenece a la ejecución registrada:

- Si sigue vivo y se confirma su identidad, se reconoce como activo.
- Si se confirma que terminó, se limpia el registro activo y su INI; se conserva el log.
- Si la identidad no puede verificarse, se informa incertidumbre sin matar procesos
  ni iniciar otro para ese servidor. Un PID por sí solo no identifica una ejecución.

El mecanismo de verificación y las pruebas de recuperación se concretan al implementar;
la falta de respuesta de salud por sí sola no autoriza a declarar terminado un proceso.

## Pendientes

Los pendientes globales se mantienen en la
[lista central](implementacion-catalogo-modelos.md#9-decisiones-que-no-deben-quedar-ocultas-en-el-código):
separan detalles técnicos y alcance aplazado. La gestión de abreviaturas de otros
orígenes no bloquea esta versión.

No confundir estos pendientes con las reglas de composición y modos ya confirmadas.
