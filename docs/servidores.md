# Servidores e instancias

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
Instalaciones posteriores no cambian silenciosamente ese predeterminado. La ausencia
por retirada posterior no autoriza a reemplazarlo automáticamente por otra instalación.

Una selección puede seguir `latest` dentro de su filtro (por ejemplo CUDA 12) o fijar
un release. Se conserva la intención además del build instalado. `backend update`
consulta novedades; `backend upgrade` instala y cambia el build actual de las selecciones
que siguen `latest`, sin sustituir releases fijados. Iniciar no consulta ni descarga
actualizaciones: resuelve el build actual instalado. Una instancia conserva el build
con el que arrancó. Un upgrade fallido no sustituye al anterior.

Los upgrades conservan los binarios anteriores con un límite configurable: cinco builds
por distribución, familia y plataforma, contando el actual. Tras un upgrade exitoso,
la limpieza retira primero los más antiguos, excepto el actual, los fijados explícitamente
y los usados por instancias activas. Si los protegidos superan el límite, se conservan
y se informa; no se rompe una selección o instancia para cumplirlo. Los externos nunca
se borran físicamente mediante esta política.

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
de `llama-server --version`. La sintaxis para declarar nombres cortos de otros orígenes
y variantes siguen pendientes. Registrar un externo del mismo origen/familia/release
que uno gestionado no crea una colisión de selección: pertenecen a formas de acceso distintas.

## Parámetros del servidor

El catálogo distingue el ámbito servidor/router del ámbito modelo/inferencia y el
destino de emisión (CLI o INI). Los valores del servidor no se mezclan automáticamente
con la herencia de modelos/presets.

`host`, `port` y `metrics` son definiciones estáticas de Coquena, independientes del
binario seleccionado y no eliminables del catálogo. Sus defaults propios son:

| Parámetro | Default |
|---|---|
| host | 127.0.0.1 |
| port | 8080 |
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
usa 8080. Coquena informa el puerto asignado. El registro guarda ese valor concreto.
Si se alcanza el máximo 65535 y no puede sumarse uno, se rechaza la creación sin
crear el registro: no se vuelve al principio ni se buscan huecos, pues puede haber
puertos reservados. El usuario puede elegir explícitamente otro puerto válido.
Modificar un puerto también respeta la unicidad entre servidores registrados.
Server unset port aplica la misma asignación automática: mayor puerto guardado
en el catálogo más uno, informando el nuevo valor. No rechaza por una colisión
con 8080 ni vuelve a ese default. Si el máximo guardado es 65535 (2^16 - 1),
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
Stop recibe el ID de instancia; no elimina la definición ni detiene otras instancias.

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
acumula un historial ilimitado. El tamaño inicial y el mecanismo de rotación quedan
pendientes de implementación; detener no elimina el último log disponible.

## Retirar un servidor

Se permite retirar cualquier servidor sólo cuando esté detenido. Se elimina su definición,
no los presets, modelos, companions ni backend asociados. Si existe una instancia activa,
se rechaza la acción indicando su ID; primero debe detenerse.
El comando es `server [nombre] remove`; sin nombre retira el servidor predeterminado.
Si se retira el predeterminado, se limpia esa designación sin elegir otro ni recrear
`default` automáticamente. Los comandos que omiten nombre informan que debe designarse
un servidor existente como predeterminado. La creación inicial de default es de primera
configuración, no una reparación automática de una eliminación solicitada.

## Pendientes

- Sintaxis de registro de variantes y nombres cortos de origen.
- Tamaño inicial/rotación de logs, tiempos de salud y terminación, recuperación de registros de procesos.
- Persistencia concreta del catálogo de servidores y de los defaults.

No confundir estos pendientes con las reglas de composición y modos ya confirmadas.
