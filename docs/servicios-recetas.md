# Servicios y recetas

Una receta declara un servicio de inferencia, recursos y presets consumidores,
o ambas responsabilidades. La pertenencia global se elige mediante importación;
el YAML no impone instalar recursos en la home de quien lo recibe.

## Nombres y destino

`name` es el nombre único de la receta, declarado en la raíz. Si se omite, se usa
el nombre de la carpeta base al registrar por primera vez la receta. Un valor
explícito vacío es inválido. No se normalizan nombres silenciosamente para hacerlos
admisibles por el router.

La receta admite como máximo un bloque `service`, cuyo nombre es el name efectivo
de raíz; no declara otro nombre dentro del bloque ni admite una lista o mapa de
varios servicios. `service` define backend y configuración del proceso.
Su backend puede tener un alias independiente mediante `service.backend.name`;
si se omite, el alias es el nombre efectivo de la receta. `backend://alias` se
resuelve dentro del ámbito elegido, y `backend://proyecto/alias` lo califica con
el nombre efectivo de la receta, sin buscar otro ámbito ni descubrir carpetas.
La importación conserva el alias y esa calificación. Un alias global ambiguo
requiere la forma calificada; no selecciona varios servicios por semejanza.
Las referencias de variante oficial, como `backend://cuda@13`, identifican la
variante, no un alias coincidente; un alias con ese nombre requiere calificación.
El alias no adopta archivos externos ni convierte un backend en oficial.
`presets` es otro bloque raíz: sus entidades se registran independientemente y se
publican en el target seleccionado. No se anidan dentro de service ni pertenecen
al servicio por estar en la misma receta. Un preset resumen de la receta noticias
se publica como `noticias/resumen` mientras pertenece al proyecto. Los modelos y
companions definidos con nombre propio local y archivos también tienen identidad de proyecto:
`noticias/principal`. Sus claves YAML y las referencias `model://principal` o
`companion://auxiliar` se resuelven dentro de ese contexto; el prefijo no renombra
archivos físicos. Una publicación remota conserva su identidad, por ejemplo
`unsloth/LFM2.5-8B-A1B-GGUF`, y su selección de cuantización/revisión; un alias
local no convierte esa publicación en un modelo propio. Los parámetros no reciben
el prefijo del proyecto.

El nombre efectivo y las entidades creadas se conservan en `.coquena/local/`;
la home mantiene su localización conocida.
La receta YAML expresa intención; el registro conserva la identidad utilizada y
las entidades creadas. Renombrar la carpeta no recalcula el nombre de receta registrado
ni renombra servicios o presets existentes. Editar name en YAML tampoco
renombra esas entidades: para usar los nuevos nombres se dan de baja y se vuelven
a cargar explícitamente. La baja de un preset publicado y la baja de un servicio
son operaciones distintas; down nunca da de baja ni detiene el servicio.
Una retirada pendiente o incierta conserva las referencias anteriores.

Para presets, la transición explícita es `down` → `up`: down retira el conjunto
con los nombres y destino guardados; una vez confirmada la retirada, up evalúa
name/carpeta actuales y registra los nuevos nombres al publicar.
No exige dar de baja el servicio para renombrar las publicaciones del consumidor.
Si el nuevo nombre colisiona, no se publica ni se recupera otro nombre de receta por
inferencia; se conserva evidencia de la retirada y se informa el conflicto.
Este ciclo sólo opera sobre publicaciones de presets.

`target`, también raíz, admite únicamente los strings lowercase `global` y `self`.
Omitirlo equivale a `global`, incluso si existe `service`; `self` exige ese bloque
y selecciona el servicio propio de la receta. No acepta nombres de servidores ni
`default` como valor.

Global es un ámbito: puede contener varios servicios. Default es la designación
de uno de esos servicios como predeterminado dentro del ámbito global; no es
sinónimo de global. Un servicio propio de proyecto no es elegible como default
global. `target: global` utiliza el servicio global marcado como default.
La elección de cuál es default pertenece a la configuración global de Coquena,
independiente de la receta consumidora. Puede asignarlo el usuario o establecerse
al preparar por primera vez el
[servidor global predeterminado](servidores.md#servidor-global-predeterminado).
Un valor de target distinto de global o self es un error de validación; no se
interpreta como nombre de servicio ni se elige otro destino.
Los nombres de servicio `global`, `default` y `self` están reservados para los selectores.

Los identificadores distinguen mayúsculas por comparación ordinal. No admiten NUL,
CR/LF ni separadores de selector `:`. Un componente de nombre de receta,
servicio o preset no admite `/` ni `\`; `/` separa el prefijo del preset publicado.
El adaptador valida además el nombre completo y diagnostica límites del build.
Un nombre inválido derivado de la carpeta requiere `name` explícito.

## Ámbitos y propiedad

Un servicio propio usa el estado del proyecto; un servicio global tiene definición,
inventario y estado bajo `COQUENA_HOME`. Como máximo hay una instancia por identidad
de servicio, no una por consumidor ni por combinación de YAML. Cada instancia
conserva su identidad verificable.

Modelos/companions y schema se resuelven en el ámbito seleccionado. Consumir un
servicio global no importa los recursos del proyecto a la home. Los pesos externos
siguen en su ruta; el backend debe poder leerla. No hay copias ni adopción implícitas.
La procedencia de una entrada conserva propietario y recursos del consumidor,
independientemente de su nombre publicado. Colisionar nombres no permite apropiarse
de una entrada de otro consumidor.

El servicio puede recibir publicaciones base globales y de varios proyectos, conservando
el registro independiente de sus presets.
Up reconcilia únicamente el conjunto del consumidor actual. Down lo retira sin
detener el servicio ni borrar archivos. Up consulta el nombre de receta y los nombres registrados localmente,
sin tratar un cambio de carpeta o intención como una baja y alta implícitas.
Copiar un proyecto crea otro consumidor local; moverlo con identidad de carpeta
comprobada conserva propietario. El nombre de carpeta o nombre de receta no prueba identidad.

## Compartir un servidor entre proyectos

Un servidor es un destino reutilizable para distintos trabajos. Las publicaciones
base globales y las de cada proyecto se combinan en él; publicar desde otro proyecto
no sustituye lo que ya ofrecen los demás. Cambiar de carpeta de trabajo tampoco
retira publicaciones. Cada contribución conserva su propietario para poder
actualizarla o retirarla independientemente.

La analogía de uso es Docker Compose: llama.cpp cumple el papel de infraestructura
de ejecución compartida; las recetas componen presets y modelos para los trabajos
de cada proyecto. Los conjuntos preset/modelo son las unidades que esos trabajos
consumen. La analogía explica la reutilización y composición, sin atribuir a
llama.cpp aislamiento de contenedores o virtualización.

Al aplicar otra combinación YAML del mismo proyecto sobre el mismo target, up
reconcilia la contribución anterior de ese proyecto con el conjunto efectivo nuevo:
agrega los presets nuevos, actualiza los modificados y retira los que dejan de
formar parte del conjunto habilitado. Los presets sin cambios se conservan y no
se fuerzan ciclos de retirada y alta. La combinación nueva no se agrega como otro
consumidor independiente ni conserva acumulados los presets de la combinación
anterior. Las contribuciones ajenas permanecen intactas. Apply/withdraw permiten
operar individualmente sobre los presets seleccionados.

Por ejemplo, un proyecto publica primero con `coquena.yaml` y después aplica
`coquena.yaml` junto con `coquena.local.yaml`. La composición efectiva de ambos
archivos pasa a definir su contribución en ese destino, con la precedencia y las
reglas de [composición](formato-proyecto.md). Los presets heredados que sigan
habilitados permanecen; añadir una capa no equivale a retirar todo lo publicado
por el archivo base. Conservar las resoluciones de ambas combinaciones tampoco
significa mantener ambas contribuciones publicadas.

Un uso habitual es un servidor global con presets de programación al que un
proyecto de procesamiento de medios agrega presets para resúmenes diarios.
El usuario puede programar mientras la aplicación solicita resúmenes al mismo
servidor. Cada trabajo selecciona sus presets y el backend administra los modelos
según demanda y configuración, compartiendo la capacidad de la instancia. También
puede utilizarse un servicio propio mediante los selectores de destino admitidos;
su ciclo de proceso sigue siendo independiente de las publicaciones.

Publicar y retirar en caliente no exige detener el servidor. No garantiza que todos
los modelos quepan en memoria ni ausencia de interrupciones durante una recarga.
La residencia, alternancia y carga ante peticiones corresponden al backend;
Coquena reconcilia presets y sólo solicita precarga cuando se declara warmup.
Las comprobaciones de actividad, decisiones de publicación y resultados siguen
el contrato de [servidores](servidores.md#plazos-y-control).

Los presets permanecen publicados hasta su retirada explícita mediante down,
withdraw o como parte de un cambio aplicado que los retire. Cambiar el target no
acredita por sí solo una retirada del destino anterior: se conserva el registro
de sus publicaciones hasta confirmar su retirada. Una retirada que forme parte
del cambio sigue las mismas comprobaciones de propietario, actividad y resultado;
no se detiene el servidor ni se afectan contribuciones ajenas.

## Ejemplos

Servicio para importación global:

```yaml
name: estable
service:
  backend:
    version: latest
    variant: cuda@12
  server:
    args:
      host: "127.0.0.1"
      port: 9931
      models-max: 1
```

Consumidor sin proceso propio:

```yaml
name: noticias
target: global
models:
  principal:
    files: [./weights/modelo.gguf]
presets:
  resumen:
    args:
      model: model://principal
      ctx-size: 32768
```

Proyecto autónomo:

```yaml
name: laboratorio
target: self
service:
  backend:
    executable: ./bin/llama-server.exe
  server:
    args:
      host: "127.0.0.1"
      port: 9932
models:
  principal:
    files: [./weights/modelo.gguf]
presets:
  prueba:
    warmup: true
    args:
      model: model://principal
      ctx-size: 8192
```

Publica `laboratorio/prueba` en el servicio propio `laboratorio`. Su definición no
registra un servicio global ni inicia el ejecutable por estar presente.

## Importación global

`import <receta.yaml>` copia la intención seleccionada al estado global, sin editar
ni seguir el original. Omisión de `--select` incluye las categorías declaradas;
el filtro admite `service,presets,model,params`. Service incluye su backend;
model incluye modelos y companions; params incluye `arg-defs`, no preferencias.
Seleccionar una categoría ausente es error. No se incorporan dependencias excluidas:
deben resolver en destino o se diagnostica antes de cambiar intención.

El servicio destino de presets es el servicio importado si se incluye `service`;
en otra importación se usa `target` y su alias global. `self` requiere incluir
`service`. `--as-default` requiere importar un servicio y actualiza el alias junto
con su definición. Al primer uso, nombre de receta se calcula contra la receta y base
originales y se registra; usos posteriores conservan esa asociación. No pasa a
ser el nombre de una carpeta interna de la home. Replace/force reemplazan contenido
seleccionado, sin autorizar por sí mismos renombrar entidades registradas.

La importación promociona las entidades locales al ámbito global: el modelo
`mi-proyecto/mi-modelo` pasa a `mi-modelo` y el preset
`mi-proyecto/mi-preset` pasa a `mi-preset`. Lo mismo ocurre con companions locales.
No conservan el prefijo del proyecto en sus identidades globales. Las publicaciones
remotas conservan sus identidades; los parámetros conservan sus claves. La promoción
no modifica ni retira entidades del proyecto original ni renombra archivos físicos.
Las referencias `model://principal` y `companion://auxiliar` se vinculan a sus identidades globales
tras validar todas las categorías seleccionadas. Nombre de receta no evita sus colisiones.
Igual nombre e igual intención normalizada es una importación idempotente; igual
nombre con contenido diferente se rechaza por defecto, mostrando el impacto.

`--replace <selector>` autoriza reemplazar una entidad en conflicto; es repetible.
`--force` autoriza reemplazar los conflictos de todas las categorías declaradas en
el archivo, por lo que no se combina con `--select` ni `--replace`. No elimina
entidades ausentes del archivo. Ninguna opción omite validación, integridad,
referencias, propiedad o protección de una instancia activa. Un reemplazo no
retira referencias ajenas ni reinicia un servicio para aplicar su nuevo backend.

Un backend oficial se prepara en el ámbito global. Las referencias externas admiten
rutas absolutas o relativas y expresiones de variables. La copia de intención conserva
las expresiones; cada comando que necesita resolverlas utiliza sus variables actuales.
Una ruta relativa resultante se resuelve contra la base original conservada localmente,
sin reinterpretarla contra la home. No se copian binarios, bibliotecas ni pesos.
Corregir una referencia puede consistir en actualizar su variable, sin editar el YAML.
Las rutas resueltas se conservan como datos locales de instalaciones y snapshots,
sin sustituir las expresiones de intención ni modificar una instancia ya iniciada.
Los comandos posteriores reciben sus variables por los mecanismos que admiten;
no reutilizan valores ni siguen el archivo env de la importación anterior.
Una última ubicación conocida sirve para diagnóstico; no sustituye automáticamente
una variable ausente ni una ruta actual inexistente y no inicia búsquedas por el disco.
Los imports JSON locales de parámetros se materializan en la copia de intención;
no quedan dependiendo de que el autor conserve esos archivos de schema.
La materialización conserva la precedencia por capa. Las cadenas JSON no pasan
a ser expresiones de interpolación por convertirse a YAML.
No se guarda un `.env` ni se congelan secretos expandidos como literales de receta.

Importar no inicia un servicio. Si está activo, publica los presets seleccionados
mediante el mismo contrato que apply; si está detenido, conserva definiciones para
su inicio. No solicitar presets no provoca reload. Un fallo de publicación distingue
intención importada de resultado remoto y conserva la operación pendiente para
reconciliar; no acredita éxito parcial como importación completamente aplicada.

## Versiones y operación

Un pin fijo permite consultar novedades, pero upgrade exige cambiar explícitamente
a latest. Cambiar a latest conserva la variante y no quita la protección del build
fijado anteriormente: retirarla es otra acción explícita. No hay actualización de
ejecución en segundo plano.

Publicación, memoria y recuperación siguen [servidores](servidores.md).
Gramática y selectores en [comandos](comandos.md); campos y composición en
[formato](formato-proyecto.md); coordinación local en
[persistencia](persistencia-proyectos.md).
