# Persistencia e identidad

## Ámbitos y layout

La base del proyecto contiene intención y `.coquena/`. La home contiene
preferencias, registro de rutas y el ámbito global importado. No se reconstruyen
estos contratos desde los catálogos históricos. Cada raíz de estado dispone de
un bloqueo de escritores y un inventario propio. Las rutas de recursos externos
nunca se convierten en destinos gestionados por estar dentro de esa raíz.

```text
COQUENA_HOME/
  config.json
  projects.json
  global/
    recipe.yaml
    consumers/<consumer-id>/recipe.yaml
    services/<service-id>/recipe.yaml
    services/<service-id>/local/
    services/<service-id>/logs/
    combinations/<id>/resolved.json
    local/
    models/
    backends/
    downloads/
    tmp/
```

`global/recipe.yaml` mantiene models, companions y arg-defs globales. Cada
contribución importada conserva nombre de receta y presets en `consumers/<id>/recipe.yaml`;
el índice global relaciona nombre publicado, consumidor y servicio destino.
Cada servicio conserva su definición en su recipe.yaml. Los IDs de directorios
son UUID locales, no nombres de usuario interpolados en una ruta. La tabla de
servicios, alias default y revisión global viven en `global/local/state.json`.
Un alias se resuelve a ID antes de preparar/operar: si cambia durante el trabajo,
no se redirige a otro servicio silenciosamente.

El layout del proyecto se describe en [formato](formato-proyecto.md). En cada raíz
`local/` contiene `state.json`, `inventory.json`, `writer.lock`, `operations/`,
`generations/` y `runtime/`. En un servicio propio la ejecución vive allí; en uno
global vive en `services/<service-id>/local/`. `runtime/` guarda INI generado,
snapshots y observaciones; nunca se escribe junto al executable externo.

`state.json` es el punto de publicación local: referencia una generación completa
e inmutable y registra revisión, identidad de ámbito y transacción. Las rutas
convencionales de inventario, índice y active son proyecciones recuperables de esa
generación, no varios commits independientes. Los lectores usan una generación
coherente; una proyección discrepante no se acepta como catálogo vacío.

## Documentos JSON

Los documentos persistidos tienen envoltura `kind`, `format`, `revision` y `data`.
`format` es el entero 1; kind distingue `combination-index`, `resolution`,
`inventory`, `state`, `active`, `last-good`, `update-plan`, `operation`, `recipe-registry`
y `project-index` (índice de ubicaciones de la home).
No es una versión de receta YAML. Lectura rechaza claves duplicadas, un format
desconocido o kind incorrecto, sin sobrescribir. Revisión entera monotónica por
documento, UUID canónico lowercase y fecha UTC RFC 3339. Codificación UTF-8 sin BOM.

Ejemplo mínimo de índice (el ID ilustrativo debe calcularse en uso real):

```json
{
  "kind": "combination-index",
  "format": 1,
  "revision": 1,
  "data": {
    "combinations": {
      "<sha256>": {
        "files": ["coquena.yaml"],
        "env-files": []
      }
    }
  }
}
```

Campos mínimos de `data` por documento:

| Kind | Datos requeridos |
| --- | --- |
| resolution | combination-id, selections (identidad de intención sin secretos), resources (key, kind, origen, commit/build, variante, artefactos e identidades de contenido tipadas). No rutas físicas privadas. |
| inventory | scope-id, resources con UUID, logical-key, ownership, installations con UUID, ubicación, archivos propios, origen explícito/automático, disponibilidad y observaciones de integridad. |
| state | scope-id, generation, transaction-id, documentos/revisiones, servicios y alias en ámbito global. |
| active | service-id, instance-id, identidad de proceso (PID, inicio e identidad de ejecutable), endpoint, combinación, snapshot, recursos físicos y log. |
| last-good | service-id, snapshot independiente, recursos exactos, checked-at y evidencia de salud. |
| update-plan | scope-id, combination-id, revisión base, identidad local de intención y selecciones actuales/propuestas exactas. |
| operation | operation-id, tipo, ámbitos, revisiones base, fase, recursos, candidatos, documentos nuevos y anteriores; remote-status cuando interviene router. |
| recipe-registry | recipes con recipe-id, archivos relativos, nombre de receta registrado, entidades creadas y revisión. Local en el ámbito de la receta. |
| project-index | projects con scope-id, base absoluta conocida, identidad física de directorio (volumen y archivo) y revisión local observada. Sólo navegación; no acredita propiedad ni sustituye el registro de recetas. |

Los campos opcionales se omiten; null no equivale a registro vacío válido.
SHA-256 de un artefacto usa algoritmo `sha256`, dominio `file-bytes` y 64 hex
lowercase; otras identidades conservan algoritmo y dominio propios. Commit no
sustituye hash. Metadatos desconocidos no prueban integridad ni procedencia.

## Identidad de combinación y rutas

El ID de combinación es SHA-256 de UTF-8 de un JSON compacto con propiedades en
este orden: `files`, `env-files`. Cada array conserva el orden solicitado. Strings
se serializan con escapes JSON mínimos, sin escapar `/` o Unicode no ASCII; no
hay espacios, BOM ni newline final. Ejemplo exacto de entrada al hash:

```json
{"files":["coquena.yaml","coquena.cpu.yaml"],"env-files":[]}
```

Las rutas se hacen relativas a base, con `/`, segmentos `.` eliminados y `..`
reducidos léxicamente. Se conserva mayúscula y Unicode del nombre de selección,
sin normalización cultural. El ID portable es identidad de selección textual,
no prueba de que dos rutas apunten al mismo archivo Windows. No incluye contenido,
carpeta absoluta, variables ni secretos. Renombrar o reordenar cambia el ID;
editar contenido o mover la carpeta no.

Una selección fuera de base usa `../` si puede relativizarse; si exige otra unidad
se registra sólo en el índice local con su ruta absoluta. Una combinación con
`.env`, capas privadas o referencias privadas sólo se publica en el índice local;
no se filtran rutas privadas a índices portables. Archivo privado explícito se
selecciona con `--private`; todo uso de `.env` hace privada la combinación.
Sin ese marcador no se publican por inferencia datos locales como portables:
se diagnostica una referencia exterior antes de escribir una resolución compartible.

La identidad física local usa ruta absoluta resuelta y comparación del sistema
de archivos; en Windows normal, ordinal sin distinguir mayúsculas. Junctions,
symlinks y aliases de volumen requieren resolver identidad de archivo/directorio
para coordinar escrituras y detectar la misma carpeta. Una igualdad textual o
que S: y D: tengan letras distintas no prueba independencia física. Antes de
borrar/publicar se comprueba pertenencia al destino autorizado y propiedad;
si no puede probarse, se conserva y diagnostica. No se borran padres por prefijo.

UUID de ámbito y consumidor se registran localmente con identidad física de base.
Una copia en otra carpeta obtiene identidad nueva aunque copie metadatos locales;
no puede retirar entradas del original. Un traslado sólo conserva identidad si
se verifica que es la misma carpeta, no por coincidencia de UUID copiado. Ante
incertidumbre no se adoptan publicaciones antiguas: se informa para acción explícita.

## Registro local de recetas

`.coquena/local/recipes.json` conserva las asociaciones de recetas del proyecto.
Usa la envoltura común, kind `recipe-registry`. Cada entrada registra recipe-id
(UUID local), archivos de receta relativos a la base, nombre de receta utilizado, nombres
y referencias de servicios/presets creados, destino, revisión y retiradas pendientes.
No depende de que la ruta absoluta o el nombre actual de la carpeta coincidan con
los de la primera carga. No guarda secretos ni se comparte como éxito local en Git.

Al usar la receta se consulta este estado antes de derivar name desde
la intención actual. Si se renombra la carpeta con `.coquena/` dentro, se conservan
el nombre anterior y las referencias necesarias para operar sobre sus entidades.
El nombre derivado de la nueva carpeta puede mostrarse como intención candidata;
no se renombra una entrada publicada por observar, editar o volver a leer YAML.
Una edición de parámetros no cambia por sí sola la identidad registrada.

La home conserva en projects.json el índice de ubicaciones conocidas para navegación
y observación. Al abrir el proyecto desde la nueva ubicación se actualiza ese índice
contra su estado local y la identidad comprobable. No se exige localizarlo desde
la ruta antigua ni se recorre el disco para descubrirlo. El estado local es la
referencia del nombre previo; la home no sustituye esa evidencia por el basename
de coquena.yaml, compartido por muchos proyectos.

Reclamar el nombre anterior significa comprobar que corresponde a la entidad y
propietario registrados, no reservarlo o apropiarse de otra entidad por coincidencia.
Una copia con `.coquena/` no autoriza retirar al original: se comprueba identidad y
registro del servicio antes de mutar. Si no puede distinguirse traslado de copia,
se informa conflicto y se conservan ambas evidencias. La igualdad del YAML no prueba
propiedad. Una edición atómica del archivo en el mismo proyecto mantiene su
referencia lógica, aunque cambie el ID físico del archivo.

El registro participa en la publicación recuperable junto al estado del servicio.
Antes de retirar se conservan nombre anterior, destino, propietario y resultado de
la operación. Una retirada incierta no borra esa información ni acredita que el
nombre quedó libre. Tras la baja confirmada y nueva alta explícitas de las entidades
afectadas puede adoptarse el nombre derivado de la intención actual; se rechazan
colisiones sin tocar entradas ajenas. El cambio no copia, borra ni redescarga pesos.
Las entidades no retiradas mantienen sus nombres. No se usa stop como sustituto de
retirar una publicación ni se confunde liberar memoria con retirar disponibilidad.

Una retirada confirmada por down marca la contribución del consumidor como retirada
y conserva el snapshot anterior para diagnóstico, sin fijar el nombre de receta de su
siguiente alta. Up posterior calcula el nombre de receta vigente y registra su publicación
sólo cuando corresponde a la nueva alta. Un servicio que siga existiendo conserva
su propio nombre; su referencia no impide el cambio de nombre de los presets ya
retirados. La residencia en memoria no cambia esta fase ni libera nombres.

Una importación global conserva su copia de intención y su registro local bajo
COQUENA_HOME/global/local/, como estado del ámbito importado. No sigue la carpeta
original después de importar. Un proyecto que consume ese servicio conserva su
propia identidad en `.coquena/`; su nombre de receta no se recalcula por mover la home.
Perder el índice global no destruye el registro del proyecto. Perder el estado local
no permite reconstruir propietario o éxito por inferencia desde el YAML.
## Secretos y vigencia

Las selecciones portables contienen expresiones originales, nunca valores
interpolados, variables de proceso ni hashes de secretos. La vigencia local se
comprueba con HMAC-SHA-256 del snapshot efectivo y clave aleatoria local de ámbito
guardada fuera de Git. Sin clave se invalida plan/huella, no se acepta igualdad
de intención ni se infiere corrupción de pesos. No hay HMAC en locks compartidos.

Un snapshot operativo o INI puede necesitar valores expandidos para el backend;
es local, con permisos del usuario y sin volcarse a logs, evidencia o resolución.
No se promete que Git ignore vuelva secreto un archivo legible por otros usuarios.
Exportar configuración efectiva redacciona expansiones por defecto.

## Coordinación y publicación recuperable

1. Capturar bytes/revisiones de intención y estado; resolver/transferir fuera del
   bloqueo del catálogo, con una exclusión separada por ejecutor de transferencia.
2. Preparar artefactos nuevos e inmutables, verificarlos y conservar revisión anterior.
3. Adquirir bloqueos entre procesos de todos los ámbitos afectados, ordenados por
   identidad física de raíz. Nunca esperar red, descarga o interacción del usuario
   reteniendo esos bloqueos. Si se necesita otro ámbito, liberar y reintentar la
   adquisición ordenada. Los leases son handles del SO, no un archivo PID que baste borrar.
4. Releer bytes de YAML/.env/schema y revisiones. Un cambio desde el snapshot
   invalida la candidata: conflicto, sin last-writer-wins. No repetir una mutación
   remota para resolver un conflicto local. La huella del origen sólo se guarda local.
5. Escribir en `local/operations/<id>/` el manifiesto de intención de commit y una
   generación completa. Flush de archivos antes de sustituir el puntero state;
   temporales y destino final de cada reemplazo están en el mismo volumen.
6. Publicar el puntero por reemplazo; conservar generación anterior y manifiesto
   hasta actualizar proyecciones e índices. Registrar fase committed. Para varios
   ámbitos, el manifiesto coordinador enumera participantes y el commit decide
   la generación conjunta; un lector que detecta participante pendiente reconcilia
   antes de exponer una combinación, sin prometer atomicidad de varios renames.
7. Liberar bloqueos; los artefactos anteriores siguen protegidos por snapshots,
   operaciones e instancias. Su eliminación sólo pertenece a limpieza explícita.

Antes de commit, una interrupción conserva el estado anterior; candidatos propios
pueden recuperarse sin adoptarlos por existencia. Después de commit se completa
publicación idempotente de documentos, sin volver a descargar ni cambiar selección.
Si falta una generación comprometida o no coincide su manifiesto, se diagnostica
y conserva evidencia; no se lanza sustituto ni se vuelve al catálogo vacío.
Una copia entre volúmenes se verifica en destino antes de publicar referencias.

Lectores toman snapshot de la generación y vuelven a comprobar puntero si necesitan
efectuar una mutación. Limpieza usa los mismos bloqueos y protege candidatos de
operaciones, referencias cruzadas, pins, activo y última buena. Registrar una
referencia de consumidor y permitir borrar su recurso deben quedar serializados.

## INI y estado remoto

El servicio mantiene un registro local por `consumer-id`, nombre publicado y
snapshot de recurso. Preparar una publicación incluye protección provisional de
sus recursos antes de exponer rutas al backend. Una operación pendiente reserva
la revisión del servicio: otros escritores pueden observar pero no publicar otra
composición encima hasta reconciliarla. No se retiene un bloqueo de archivo durante HTTP.

Se genera INI completo con entradas propias del servicio y de todos sus consumidores;
sólo se cambian las del conjunto seleccionado. El manifiesto conserva INI anterior,
candidato, revisiones y fases `prepared`, `sent`, `observed`, `uncertain`.
Se publica el INI y solicita reload una vez. El catálogo observado confirma resultado
cuando permite atribución; respuesta aceptada sin resultado observable se informa
como no confirmada. Si Coquena se cierra entre envío y registro, se observa antes
de decidir: no se reenvía automáticamente una mutación de resultado incierto.

Down utiliza el conjunto publicado registrado, aunque el YAML se haya editado;
no necesita descargar ni preparar recursos. Si el servicio está detenido, retira
su contribución persistida para el próximo inicio y lo informa sin HTTP. Si está
vivo pero inaccesible, mantiene protección de recursos que todavía podría usar.
Referencias se liberan sólo cuando se confirma retirada o terminación de la instancia.

Restauración conserva entradas ajenas y sigue [servidores](servidores.md#restauración-acotada-de-presets).
No se equipara commit local a transacción del router ni se reinicia para ocultar
un resultado incierto. Otro consumidor o un operador del backend puede cambiar
estado remoto: se reobserva y diagnostica diferencia, sin adoptar entradas ajenas.
