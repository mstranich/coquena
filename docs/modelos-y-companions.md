# Catálogo de modelos y companions

Inventario por proyecto, selección, propiedad e integridad. Los diagramas de disponibilidad están en [estados.md](estados.md).

La [guía de implementación](implementacion-catalogo-modelos.md) descompone estas reglas
en contratos e invariantes técnicas. Los mecanismos propuestos
allí no sustituyen las decisiones funcionales de este documento.

## Ejes independientes

| Eje | Pregunta | Comprobación |
|---|---|---|
| **Disponibilidad** | ¿Están presentes y son legibles los archivos necesarios? | Local, sin hash completo |
| **Seguimiento remoto** | ¿La selección instalada coincide con lo publicado en el origen seguido? | Metadatos remotos; no aplica a externos |
| **Integridad** | ¿Los bytes coinciden con la referencia local registrada? | Hash completo, explícito |

La propiedad (**gestionado o externo**) es un atributo del registro. Perder el
seguimiento remoto no vuelve inutilizable un modelo disponible; tampoco estar al día
demuestra integridad ni garantiza que el backend pueda cargarlo.

Un modelo ocupa uno o varios archivos GGUF. Sus shards forman una unidad para
registrar, actualizar, comprobar disponibilidad y eliminar. Un companion es una unidad
independiente y opcional: no se descarga por acompañar al modelo en el repositorio.
Coquena descarga sólo la selección solicitada y sus shards necesarios, no el repositorio
completo.

La selección inicial y el menú de descarga están en [comandos.md](comandos.md#model).
Una cuantización explícita con una única unidad válida inicia descarga sin menú
ni oferta de README; la unicidad se evalúa por unidad, no por archivo o shard.
La descarga explícita de un modelo no incorpora companions por proximidad remota.
Al preparar un proyecto se incluyen los companions declarados por sus presets;
no se presupone su repositorio ni compatibilidad. Los algoritmos y las interacciones se detallan en la guía técnica
y en secuencias.md.

Los nombres son pistas, no un requisito para reconocer un GGUF. La selección evalúa
también archivos de nombre arbitrario mediante metadatos remotos acotados en el commit
resuelto. Separa cuantización base y calificadores del publicador de tokens como `it`,
`instruct` o `chat`. Conserva etiquetas respaldadas como `IQ1_S-XL`; `general.file_type`
confirma la base, sin certificar la receta del calificador. Un conflicto entre nombre
y cabecera impide crear la operación. Si falta `file_type` o su ID es desconocido,
una etiqueta reconocida sigue siendo candidata por nombre; sin etiqueta resoluble se
informa incertidumbre, sin inventar nombre local.

Las recetas dinámicas documentadas por el publicador (por ejemplo `UD-Q4_K_XL`)
conservan su etiqueta completa y condición de candidato. Su `file_type` predominante
se registra como evidencia independiente: no renombra la receta ni prueba su mezcla.
Una receta documentada no se equipara automáticamente a Q4_K_M por ese metadato.

Auxiliares `mmproj`/`mtp` se distinguen por tokens de nombre/carpeta y pistas de cabecera
cuando existen; no se convierten en principales por carecer de cuantización en el nombre.
Para MTP, los marcadores delimitados `mtpdraft`, `mtp-draft` o `mtp-only`, una carpeta exacta `mtp`
o el prefijo `mtp-` al comienzo del nombre del archivo producen candidatos auxiliares.
Un token interno `MTP` por sí solo conserva la candidatura principal, incluido un
principal con MTP integrado. Las pistas de nombre/carpeta no certifican el rol de
draft ni su compatibilidad; el menú muestra esa condición incluso cuando la cabecera
confirma la cuantización. Un nombre arbitrario dentro de `mtp` puede resolver su
cuantización por cabecera; sin marcador ni evidencia específica no se inventa un rol MTP.
Una cabecera genérica (`general.type=model`), metadatos específicos ausentes o un
publicador distinto son evidencia insuficiente, no contradicciones por sí solos.
Se considera incompatible un marcador MTP frente a arquitectura `clip`, marcadores
auxiliares de tipos diferentes, una cuantización estándar discordante entre nombre y
cabecera o metadatos incompatibles entre shards. Esos conflictos impiden la selección;
la ausencia de cuantización resoluble produce un resultado no concluyente.
Dos unidades con igual cuantización conservan opciones separadas por rutas. Antes del GUID
se comprueban las cabeceras de todas las partes elegidas y la coherencia split; esto no
certifica integridad de los pesos ni compatibilidad de carga. Un fallo aislado conserva
los demás candidatos; una exploración limitada se informa como parcial/no concluyente.

## Identificación en el catálogo local

El **nombre canónico** identifica la selección en el inventario del proyecto.
No debe confundirse con los nombres/aliases de entradas publicados en el router:

```text
usuario/repositorio:cuantización
usuario/repositorio#tipo:cuantización
```

Ejemplos: `ggml-org/Qwen3.8-27B-GGUF:Q4_K_M` y
`ggml-org/Qwen3.8-27B-GGUF#mmproj:BF16`. El principal omite `#tipo`.
La cuantización del companion es independiente de la del principal; puede compartirse
entre cuantizaciones y modelos sin duplicar su registro. `#tipo` es una extensión
de Coquena; `:cuantización` sigue la convención de llama.cpp.

Los nombres locales son case insensitive y conservan su escritura. Las rutas remotas
se conservan exactamente como las informa el origen. Una colisión se rechaza; también
se rechaza registrar el mismo conjunto externo con otro nombre, sin exigir deduplicación
por hash. Un nombre retirado puede reutilizarse, sin reconectar referencias rotas.

El nombre no es una ruta física ni el nombre del preset. Las configuraciones referencian
el registro; el INI recibe rutas absolutas. Cada registro tiene un ID interno estable
para mantener referencias ante renombres. La resolución física distingue revisiones para conservar recursos activos y anteriores.
Los locks transportan selecciones; los identificadores locales no presuponen una home compartida.

## Presets

Cada preset pertenece a un modelo principal y guarda sus propios valores de parámetros.
Un modelo puede tener varios presets, por ejemplo uno con contexto corto y otro con
contexto largo. Registrar el modelo no crea valores compartidos que hereden sus presets.

Las definiciones proceden del [catálogo de parámetros](catalogo-de-parametros.md).
Asignar un valor modifica sólo ese preset; quitarlo elimina la asignación, sin recuperar
valores globales, por familia ni por modelo. Los parámetros del proceso pertenecen a la
[definición del servidor](servidores.md#parámetros-del-servidor).

Los companions y modelos auxiliares se eligen mediante `preset bind`, indicando el
parámetro que recibe su ruta y el nombre del recurso registrado. El vínculo
se guarda por ID estable y se resuelve a ruta absoluta al generar el INI. Un literal
explícito continúa siendo literal aunque coincida con el nombre de un registro.
En args de presets también se admite un selector canónico remoto completo sin
prefijo, sin declarar previamente un alias; véase [formato del proyecto](formato-proyecto.md#modelos-companions-y-referencias).
Coquena comprueba
referencias y archivos; la compatibilidad funcional de la combinación la decide el backend.

Clonar un preset copia sus valores y referencias al modelo destino, sin copiar archivos
ni crear herencia. El clon es independiente: cambios en el origen no se propagan.
La sintaxis está en [comandos](comandos.md#preset).

Un vínculo admite parámetros sin definición o definidos como texto de ámbito modelo;
no modifica su tipo en el catálogo. La definición de texto expresa el argumento que
recibirá el backend; la asignación del preset distingue literal de vínculo. Los alias
se normalizan a la identidad del parámetro. Un vínculo puede seleccionar un modelo
principal como auxiliar, por ejemplo para un argumento de borrador; `bind model`
selecciona exclusivamente el principal del preset y conserva sus demás asignaciones.
La configuración de métodos de especulación es independiente del vínculo a archivos:
no se deduce un método ni compatibilidad a partir del tipo o nombre del recurso.

El modo permisivo admite desconocidos y valida conocidos; el estricto exige definición para todos. Se comprueban tipos, ámbito,
aplicabilidad y dependencias: una asignación inválida se rechaza con diagnóstico,
no se omite para iniciar otra configuración silenciosamente. Las referencias
seleccionadas determinan los recursos requeridos y protegidos.

## Disponibilidad, en el núcleo

- **Disponible:** la ruta existe, es legible, el encabezado GGUF se lee y, si es un
  conjunto de shards, están todos.
- **Incompleto:** faltan shards.
- **Inaccesible:** la ruta no existe o no se puede leer (disco desmontado, archivo
  movido, permisos).
- **Inválido:** no es un GGUF legible o sus metadatos de división contradicen el conjunto.

Ninguno de los cuatro exige calcular un hash completo. La integridad se comprueba de forma explícita y separada.

**Cambio detectado (aviso, no estado):** si el tamaño o la fecha de modificación difieren
de lo registrado, Coquena avisa y no bloquea. La consulta no actualiza el registro ni
afirma que los bytes sigan íntegros. El registro se actualiza sólo cuando el usuario
lo pide, con `model refresh`, que relee los metadatos necesarios y anota el tamaño y la
fecha actuales.

## Modelos divididos en shards

Un conjunto dividido tiene un único registro y una lista ordenada de archivos esperados.
Por ejemplo, `DeepSeek-V4-Flash-IQ1_S-XL-00001-of-00002.gguf` y
`DeepSeek-V4-Flash-IQ1_S-XL-00002-of-00002.gguf` forman una selección
`persadian/DeepSeek-V4-Flash-GGUF:IQ1_S-XL`, no dos modelos ni un companion.
Se conserva la etiqueta de variante completa; no se reduce `IQ1_S-XL` a `IQ1_S`.

- **Descubrimiento:** agrupar por carpeta, prefijo de variante y sufijo numerado
  `-00001-of-00002.gguf`; exigir índices consecutivos 1…N y total consistente.
  No agrupar por semejanza de nombres ni mezclar variantes. Si hay varias agrupaciones
  candidatas, pedir selección explícita. Un nombre ambiguo no autoriza a inventar un conjunto.
- **Comprobación local:** leer metadatos GGUF de división en las partes presentes y
  contrastar cantidad e índices con los nombres. Una contradicción se diagnostica como
  inválida; una parte esperada ausente, como incompleta. Estas lecturas no prueban integridad.
- **Alta externa:** el usuario indica el primer shard; localizar sus hermanos en la misma
  carpeta. Registrar el conjunto esperado y diagnosticar partes ausentes. El registro puede
  quedar incompleto, pero no se permite usarlo hasta disponer de todas sus piezas legibles.
  No buscar hermanos recursivamente ni copiar o reparar archivos externos.
- **Carga:** resolver la referencia al primer shard y conservar hermanos con los nombres
  esperados en la misma carpeta. llama.cpp carga el conjunto; Coquena no fusiona los pesos.
- **Gestionados:** descargar todas las partes desde el mismo commit, controlar cada archivo
  y activar sólo el conjunto completo verificado. Actualización y eliminación operan sobre
  la unidad; progreso e integridad se informan también por archivo.

GGUF no exige un CRC de los pesos equivalente al de un archivo comprimido. Nombres y
metadatos permiten comprobar completitud y estructura, no certificar que partes externas
provengan de la misma revisión. La integridad requiere hashes completos contra referencias
conocidas; una base calculada en un externo sólo permite detectar cambios posteriores.
Coquena no replica las comprobaciones de tensores ni la carga de inferencia del backend.

### Límites de la convención GGUF

Los nombres descubren candidatos; los metadatos comprueban estructura; los hashes
comprueban integridad contra una referencia. La expresión regular de la convención
GGUF no es un validador universal: un nombre no convencional no vuelve inválido un
archivo legible. Si no puede determinarse el conjunto sin ambigüedad, se consulta al
usuario; no se agrupan partes por semejanza ni se inventa una identidad.

`mmproj` puede servir a visión/audio y `mtp` es otro prefijo auxiliar documentado.
Los metadatos de nombre, versión, UUID y repositorio son pistas, no hashes ni pruebas
de revisión remota. Los repositorios del modelo de origen no sustituyen al repositorio
que publica los GGUF seleccionados. Una misma etiqueta de cuantización puede describir
varias variantes; se seleccionan explícitamente las rutas concretas.

Para el descubrimiento automático:
- Conjuntos divididos con nombres no convencionales quedan fuera del registro automático;
  se diagnostica la limitación, no se declara inválido el GGUF. No se inventa una lista de partes ni su orden.
- Si hay varias variantes para un selector, el usuario elige las rutas concretas. Sólo
  una variante puede estar registrada por nombre canónico; otra alta colisiona y se rechaza.

## Renombres y reubicaciones remotas

Un renombre remoto confirmado conserva la identidad lógica y las referencias.
Nombre parecido no prueba equivalencia: se exige correspondencia de contenido.
Update registra el plan; upgrade lo aplica mediante preparación separada, sin
mover ni sobrescribir archivos que una instancia esté utilizando. Los locks y
el inventario resuelven cada revisión física; los nombres de carpetas no son la
identidad del recurso. No se mueven archivos externos.

## Quitar un modelo o companion

`model remove` y `model companion remove` quitan la unidad correspondiente del registro.
Si Coquena descargó sus archivos y los
guarda en su área gestionada, **borra físicamente todos los archivos propios** e informa
cuántos bytes liberó. Si la unidad es externa, **no toca sus archivos** e informa que
permanecen en disco; así no afecta a LM Studio ni a otra herramienta que los gestione.
Eliminar sólo los archivos propios de la unidad, nunca toda la carpeta del repositorio
indiscriminadamente: puede contener otras cuantizaciones o companions. Retirar carpetas
únicamente si quedan vacías, sin tocar archivos o registros vecinos.
Si una eliminación física falla, informar archivos eliminados y pendientes con sus
causas; conservar la información necesaria para reintentar sin perder la propiedad
de los pendientes. No anunciar retiro completamente finalizado ni borrar silenciosamente
esa información. Contar sólo bytes de archivos efectivamente eliminados, no el tamaño
previsto de toda la unidad. La eliminación parcial no convierte el resto en externo
ni autoriza a retirar archivos de otras unidades.
La unidad con retirada parcial permanece registrada como pendiente y no servible,
con su identidad, propiedad e inventario originales. Repetir el mismo comando reintenta
los archivos restantes; los archivos ya ausentes no suman bytes liberados en ese intento.
Mientras esté pendiente no se permite iniciar ni modificar su instalación o trasladarla.
El nombre queda reservado hasta completar la retirada; los presets conservan sus IDs.
La misma regla de propiedad rige para backends. Los presets que referencian una unidad
retirada no se borran: pasan a **No servible** por *referencia rota*.
La retirada se rechaza si una instancia activa utiliza la unidad, indicando sus IDs;
referencias guardadas sin uso por instancias activas no bloquean el retiro. Véase
[servidores.md](servidores.md#inicio-y-detención). Preparar una revisión nueva no elimina ni reemplaza la revisión en uso.

El inventario y la retirada pendiente pertenecen al proyecto. Antes de borrar se
persiste la propiedad y la lista de archivos; una interrupción permite reintentar.
Quitar explícitamente un recurso no equivale a desactivar un preset ni a limpiar
recursos sin referencias. La limpieza respeta además las protecciones de locks,
combinaciones conocidas y recuperación descritas en
[actualización y limpieza](actualizacion-y-limpieza.md).

## Companions

Companion es la categoría auxiliar; `mmproj` es un proyector multimodal de visión/audio,
no el nombre de toda la categoría. Registro, listado, refresh, update, upgrade,
integridad y eliminación reutilizan las reglas de modelos bajo `model companion`.
La descarga selecciona `usuario/repositorio#tipo[:cuantización]` con el mismo menú,
limitado al tipo solicitado; no descarga el principal ni configura un preset.

Un companion tiene **exactamente los mismos estados** que un modelo. Lo propio del
companion no es su estado sino su relación con los modelos:

- **No depende de un modelo principal para estar disponible.** Un proyector sin su
  modelo instalado sigue disponible.
- **Registro:** `model companion add <ruta.gguf> --name <usuario/repositorio#tipo:cuantización> [--try-match]`.
  El tipo está en el nombre, sin `--type`; no configura un preset.
- **Retiro:** `model companion remove <nombre>` resuelve el registro por su nombre canónico.
- **Uso:** un parámetro del preset referencia al companion registrado. Cada preset
  guarda su propia referencia; Coquena la resuelve a una ruta absoluta
  en el INI. No hay una segunda lista de asociaciones en el preset.
- **Compatibilidad funcional:** queda a cargo del backend al cargar la combinación.
  Coquena no la certifica ni bloquea la configuración por esa causa. No hay un modo
  forzado: registrar y configurar los archivos basta para intentar utilizarlos.
- **Un companion puede servir a varios modelos**, y un modelo puede tener varios
  companions (proyector, borrador).

Compartir significa referenciar el mismo registro, no crear varios registros propietarios
del mismo archivo. No se introduce deduplicación física entre repositorios ni almacenamiento
compartido por hash como requisito del proyecto. Cada unidad gestionada tiene archivos propios;
si otra unidad pretende apropiarse de una ruta ya registrada, se rechaza la colisión.
Retirar un companion compartido conserva los presets, pero sus referencias quedan rotas.

## Preset: servible o no

El estado que realmente importa al lanzar no es el del modelo sino el del **preset**, y se
deriva de la disponibilidad de sus archivos:

- **Servible:** su modelo y los companions referenciados por su configuración efectiva
  están disponibles. No garantiza que el backend consiga cargarlos.
- **No servible:** alguno no está disponible, o la referencia está rota porque se quitó
  del registro. El inicio del servidor se rechaza, sin omitir el preset; véase [servidores.md](servidores.md).

## Incorporación: externo o gestionado

### Reutilizar archivos obtenidos por otra herramienta

El alta pertenece al inventario del proyecto seleccionado. El usuario puede copiar
o mover a su carpeta archivos ya descargados con LM Studio, Ollama u otra herramienta,
registrarlos y vincularlos a un preset sin descargar otra vez sus gigabytes.
También puede registrar una ubicación ajena explícita; esa referencia depende de
que la ubicación siga disponible y no es autocontenida por compartir el YAML.

Si el usuario trajo una copia, registrar la copia actual y usarla desde el proyecto,
no la ruta de origen del otro gestor. Las referencias dentro de la carpeta se
expresan respecto de la base del proyecto para conservar portabilidad. Se requieren
todos los shards necesarios y los companions que seleccione la configuración.
No rastrear automáticamente las bibliotecas de otros gestores ni mover sus archivos.

Externo describe propiedad, no posición física ni un catálogo global: estar dentro
de la carpeta no autoriza a Coquena a borrar o actualizar el archivo. El registro
local permite utilizarlo y verificarlo con el mismo criterio que otros externos.
Sin baseline de integridad se informa esa ausencia; no se exige una descarga nueva
para poder registrar y usar una selección local disponible.

El matching opcional consulta el repositorio indicado y conserva candidatos,
evidencia y elección en este proyecto. No requiere éxito para usar el archivo.
Equivalencia de nombre, cabecera y tamaño no certifica identidad de bytes ni
revisión remota; no convierte por sí sola el recurso en gestionado ni habilita upgrade.
Una referencia local compartida no promete poder reconstruirla desde Hugging Face
si su correspondencia exacta no está demostrada. El flujo no confunde ahorrar una
descarga con certificar procedencia o adquirir propiedad.

Registrar un externo no requiere identificar su revisión remota ni calcular un hash
completo. Basta la referencia del catálogo a sus rutas: no necesita symlinks ni hardlinks.
Si otra herramienta mueve, elimina o reemplaza esos archivos, Coquena informa lo que
observa; no los repara, actualiza ni adopta automáticamente.

El alta exige `--name <nombre-canónico>`: declaración del usuario, no prueba de procedencia.
Sin `--try-match`, el registro es exclusivamente local. `--try-match` es un flag opcional
sin valor: intenta emparejar la selección en el repositorio declarado usando metadatos
locales. Fallo de red, ausencia de coincidencias o ambigüedad no invalidan el registro
ni cambian su nombre. Si hay candidatos, la terminal muestra sus identificadores y
razones de equivalencia en un menú de selección explícita, similar al de cuantizaciones,
con opción de cancelar el emparejamiento. Elegir uno guarda la equivalencia informativa
seleccionada, sin certificar identidad de bytes o revisión ni cambiar automáticamente
el nombre del registro. Cancelar deja el registro local concretado, sin emparejamiento.
El registro local se persiste antes de presentar ese menú; no se exige elegir un candidato.
Sin terminal interactiva se omite el intento. Las consultas se limitan a metadatos del
repositorio declarado, con un presupuesto de 30 segundos y hasta 128 candidatos en el
menú. Se comparan rol y cuantización candidatos por nombre remoto, número de archivos y
tamaños por parte, descartando contradicciones conocidas con la cabecera local. No se
descargan pesos ni se calculan hashes completos para emparejar. La equivalencia conserva
los archivos remotos, la revisión consultada, las razones y la fecha de selección por
separado del seguimiento gestionado; el listado la identifica como no certificada.
No reconstruye procedencia exhaustivamente ni habilita update/upgrade del externo.
Una descarga gestionada posterior deja intactos los archivos externos.

### Pasar un externo a gestión de Coquena

Si una descarga solicita un nombre canónico ocupado por un externo, ofrecer reemplazar
explícitamente ese registro por la instalación gestionada. No es una excepción silenciosa
a las colisiones: rechazar la propuesta o cancelar mantiene el externo sin cambios.
Otras colisiones continúan rechazándose.

Hasta completar y verificar toda la descarga, el registro externo sigue vigente.
Sólo al publicar se cambia su propiedad y rutas a la instalación gestionada, conservando
su ID y las referencias de presets/configuración. Fallos o interrupciones previas no
retiran el externo. Nunca borrar, mover ni adoptar sus archivos originales.
Antes de publicar, comprobar que el registro de partida no cambió durante la operación.
La operación reserva la unidad hasta reanudarla o cancelarla: no puede iniciarse una
instancia que la use ni modificar sus metadatos mientras está pendiente. Si una
instancia ya usa el externo, detenerla antes de comenzar la transición. El destino
gestionado no puede coincidir con las rutas externas originales. Cancelar después
de publicar conserva la instalación gestionada y completa la limpieza de preparación.

## Seguimiento y actualización de gestionados

El registro conserva el repositorio, la referencia seguida (normalmente una rama como
`main`), la selección de archivos, el commit de descarga y, por archivo, rutas, tamaño
e identidad de contenido con su tipo. Conserva por separado el resultado y momento de
la última consulta y la revisión candidata. No confunde revisión instalada con consultada.

Se sigue el repositorio que publica los archivos seleccionados: actualizar el modelo
original no implica que un tercero haya regenerado su GGUF. Un cambio de pesos por sí
solo no cambia el nombre local; un renombre remoto confirmado sigue las reglas de renombres.

### Reglas de correspondencia

| Observación | Resultado |
|---|---|
| Misma ruta y contenido | Sin cambios |
| Misma ruta, contenido diferente | Actualización disponible |
| Ruta desaparecida y una única coincidencia exacta de contenido en otra | Reubicación identificada, sin volver a descargar |
| Varias ubicaciones posibles | Pedir cuál seguir |
| Ruta desaparecida y candidatos con contenido diferente | Proponer candidatos, sin afirmar sucesión |

Nombre, tamaño similar o eliminación y alta en el mismo commit son pistas, no pruebas.
No se cambia automáticamente de cuantización. En un modelo dividido se resuelve el
conjunto completo; no basta identificar un shard. La historia remota sólo se consulta
de forma acotada cuando aporte evidencia útil: el flujo normal compara inventarios.

Update y upgrade siguen el contrato de
[actualización y limpieza](actualizacion-y-limpieza.md). La selección procede de
la combinación de YAML indicada y de filtros explícitos, no de todos los recursos
de la máquina. Una consulta fallida o ambigua no equivale a estar actualizado.
Se conserva la fecha de observación y se solicita selección cuando no hay una
correspondencia inequívoca; nunca se cambia de cuantización por inferencia.

La pérdida confirmada de origen sin sucesor deja el recurso huérfano de seguimiento,
pero no impide usar bytes locales completos. Upgrade aplica el plan fijado y verifica
el conjunto seleccionado antes de publicar sus resoluciones. Puede preparar revisiones
mientras funciona la anterior; no sustituye archivos en uso ni publica shards mezclados.
El YAML no se modifica. Una nueva intención explícita se prepara al iniciar aunque no
haya un plan update previo; esto no autoriza renovar latest en cada arranque.

## Integridad local

El algoritmo local acordado es **SHA-256 completo por archivo**.
`model verify <nombre>` y `model companion verify <nombre>` verifican la unidad
seleccionada, sin red. Informan por archivo y por conjunto, distinguiendo discrepancia,
error de lectura y ausencia de referencia. No reparan ni crean una referencia implícita.

Al descargar se calcula y registra evidencia de integridad y se contrasta con la
identidad de contenido remoto cuando sea comparable. La comprobación posterior lee los
archivos completos y compara contra esa referencia. Registra el resultado y su fecha:
no certifica indefinidamente que el disco sigue intacto.

Una discrepancia se informa **sin sustituir el hash de referencia por el del archivo
alterado**. `refresh` no restablece esa referencia ni declara integridad.

En externos, establecer una referencia de integridad es opcional y explícito. Sólo
permite afirmar después «no cambió desde aquella lectura», no que la descarga original
fuera correcta o coincidiera con Hugging Face. No es requisito para registrarlos.
Establecer o sustituir esa referencia es una operación explícita distinta de
comparar integridad; no se presenta como reparación ni como verify exitoso.
Verify no tiene un modo de arreglo automático. En gestionados no se reemplazan
los hashes conocidos por los de bytes que han fallado.

La base se publica para la unidad completa: si falla una parte, se conservan todas
las referencias anteriores. El resultado distingue la creación explícita de base
de una comparación exitosa. La observación registra fecha, resultado del conjunto
y resultados por archivo; conserva además la fecha de establecimiento de la base
externa aunque se realicen comparaciones posteriores. Ni esa observación ni refresh
cambian el seguimiento remoto.

Seguimiento remoto e integridad tienen propósitos distintos, pero pueden compartir
algoritmo cuando corresponda. No se inventa un hash corto ni se confunden commit,
identificador Git, ETag y hash de los bytes. Verify nunca establece una base por omisión.

## Límites del catálogo

- No reconstruir obligatoriamente la procedencia de externos ni recorrer historiales exhaustivos.
- No escribir sidecars por archivo o shard.
- No inferir companions por proximidad; preparar los declarados en la configuración efectiva.
- No deducir cambios de pesos solamente de un commit distinto.
- No mezclar seguimiento remoto, integridad y disponibilidad en un estado único.

El alta intenta el emparejamiento sólo con `--try-match`; su éxito no condiciona el registro.
El [administrador de descargas](descargas.md) especifica transferencias, seguimiento
por archivo, pausa, reanudación, cancelación y recuperación.
## Referencias técnicas

- [Hugging Face: inventarios y metadatos por revisión](https://huggingface.co/docs/huggingface_hub/package_reference/hf_api#huggingface_hub.HfApi.list_repo_tree).
- [Hugging Face: revisión, ETag y metadatos de descarga](https://huggingface.co/docs/huggingface_hub/package_reference/file_download).
- [GGUF: metadatos y referencias de origen](https://github.com/ggml-org/ggml/blob/master/docs/gguf.md).
- [llama.cpp: carga de partes y comprobaciones estructurales](https://github.com/ggml-org/llama.cpp/blob/master/src/llama-model-loader.cpp).
- [Ejemplo de variante dividida IQ1_S-XL](https://huggingface.co/persadian/DeepSeek-V4-Flash-GGUF/tree/main).

## Integridad y arranque del proyecto

Verify comprueba YAML y existencia/integridad de backend, modelos y companions
seleccionados. Registra fallos por recurso. Un fallo conocido bloquea otro arranque
que lo seleccione en ambos modos, sin detener la instancia ya activa. No se intenta
una recuperación que eluda ese fallo. Un verify correcto o una redescarga terminada
y verificada lo resuelven; excluir el preset evita seleccionarlo, sin declararlo sano.

Up valida sintaxis, esquema y referencias efectivas y consulta fallos conocidos;
no recorre gigabytes para recalcular hashes en cada ejecución. Corrigir el YAML
se comprueba en otro up. La incompatibilidad de hardware es otro diagnóstico,
no un fallo permanente de integridad. Refresh de metadatos no borra estos resultados.
