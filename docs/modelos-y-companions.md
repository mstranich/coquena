# Catálogo de modelos y companions

Registro, actualización e integridad. Los diagramas de disponibilidad están en [estados.md](estados.md).

La [guía de implementación](implementacion-catalogo-modelos.md) descompone estas reglas
en contratos, algoritmos, pruebas y entregas para agentes. Los mecanismos propuestos
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
Cada companion se descarga en otra invocación; no se presupone su repositorio ni
compatibilidad. Los algoritmos y las interacciones se detallan en la guía técnica
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
Dos unidades con igual cuantización conservan opciones separadas por rutas. Antes del GUID
se comprueban las cabeceras de todas las partes elegidas y la coherencia split; esto no
certifica integridad de los pesos ni compatibilidad de carga. Un fallo aislado conserva
los demás candidatos; una exploración limitada se informa como parcial/no concluyente.

## Identificación en el catálogo local

No se usan aliases. El **nombre canónico** identifica la selección local:

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
para mantener referencias ante renombres. Su esquema concreto sigue pendiente.
Cada modelo principal obtiene una configuración inicial vacía al registrarse, como
raíz de herencia de sus presets. No requiere otro comando ni un archivo separado.

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

Para el alcance inicial:
- Conjuntos divididos con nombres no convencionales quedan fuera del registro automático;
  se diagnostica la limitación, no se declara inválido el GGUF. No se incorpora un flujo
  manual de partes y orden en esta iteración.
- Si hay varias variantes para un selector, el usuario elige las rutas concretas. Sólo
  una variante puede estar registrada por nombre canónico; otra alta colisiona y se rechaza.

## Renombres y reubicaciones remotas

Coquena sigue los nombres actuales del origen: un renombre confirmado del repositorio
actualiza el nombre canónico; una reubicación confirmada de archivos actualiza sus rutas
seguidas. En almacenamiento gestionado, carpetas y archivos se renombran o reubican
físicamente para reflejar el origen, sin volver a descargar contenido sin cambios.
No se conserva deliberadamente una estructura física con nombres remotos antiguos.

El registro interno y las referencias de configuración conservan su identidad durante
la operación. Un renombre no crea otro modelo ni implica un cambio de pesos. Se comprueba
la correspondencia de contenido separadamente; no basta la semejanza de nombres.
La regla de seguimiento de nombres no autoriza mover archivos externos.

`update` detecta y guarda el cambio; no mueve archivos. `upgrade` aplica el cambio físico
en gestionados junto con la actualización correspondiente. Debe resolver colisiones,
fallos e interrupciones sin sobrescribir registros o archivos ajenos ni dejar referencias
inconsistentes. Si la unidad está en uso, upgrade se rechaza antes de descargar o mover.
El protocolo físico y su coordinación con el índice requieren pruebas de recuperación.

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
La misma regla de propiedad rige para backends. Los presets que referencian una unidad
retirada no se borran: pasan a **No servible** por *referencia rota*.
La retirada se rechaza si una instancia activa utiliza la unidad, indicando sus IDs;
referencias guardadas sin uso por instancias activas no bloquean el retiro. Véase
[servidores.md](servidores.md#inicio-y-detención). Upgrade y renombres también se bloquean en uso.

El catálogo central es `<home>/catalogs/models.json`, sin sidecars por archivo o shard.
Los pesos siguen `models/<usuario>/<repositorio>/<ruta-remota>`, sin carpetas propias
por cuantización. La estructura completa está en la guía técnica. Recuperación del índice
y representación técnica de eliminaciones parciales requieren diseño; las reglas de
propiedad y diagnóstico ya están definidas arriba.

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
- **Uso:** un parámetro de configuración referencia al companion registrado. El preset
  puede heredar esa referencia o sobrescribirla; Coquena la resuelve a una ruta absoluta
  en el INI. No hay una segunda lista de asociaciones en el preset.
- **Compatibilidad funcional:** queda a cargo del backend al cargar la combinación.
  Coquena no la certifica ni bloquea la configuración por esa causa. No hay un modo
  forzado: registrar y configurar los archivos basta para intentar utilizarlos.
- **Un companion puede servir a varios modelos**, y un modelo puede tener varios
  companions (proyector, borrador).

Compartir significa referenciar el mismo registro, no crear varios registros propietarios
del mismo archivo. No se introduce deduplicación física entre repositorios ni almacenamiento
compartido por hash en la primera versión. Cada unidad gestionada tiene archivos propios;
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

`model update [nombre]` consulta y guarda novedades; no cambia pesos ni su revisión de
descarga. Puede registrar una reubicación inequívoca sin alterar las rutas locales.
Un externo explícito informa que no aplica. Sin nombre, consulta todos los gestionados
del catálogo, incluidos companions; los externos se omiten indicando que no aplica.
`model companion update` sin nombre limita esa consulta a companions.

Una consulta fallida significa **no se pudo comprobar**; una correspondencia ambigua,
**requiere intervención**. Ninguna equivale a «actualizado» ni necesariamente a
«desactualizado». Se conserva la última observación con su fecha, sin presentarla como
una comprobación actual.

Si la consulta permite confirmar pérdida del origen o de la selección seguida y no
hay sucesor identificado ni candidatos, informar **huérfano de seguimiento**: no puede
actualizarse con el seguimiento actual. No reconstruir procedencia ni elegir otra
cuantización para remediarlo. Si hay candidatos, ofrecerlos para elección explícita;
cancelar conserva instalación y seguimiento sin resolver, sin descargar pesos.
El menú muestra candidatos, diferencias y la acción de cancelar. Elegir uno establece
explícitamente el destino de seguimiento y guarda el plan del commit seleccionado;
no prueba identidad retrospectiva ni modifica pesos. `upgrade` aplica ese plan.
Si la elección implica cambiar el espacio de nombres o las rutas remotas, upgrade
también renombra/reubica carpetas y archivos físicos gestionados para reflejarlo,
conservando IDs y referencias y comprobando colisiones. Update no mueve archivos.
Un error de red o permisos no demuestra orfandad. La pérdida de seguimiento no bloquea
la inferencia: archivos completos y legibles continúan disponibles localmente.

`model upgrade [nombre]` aplica la revisión concreta detectada por `update`, no una
revisión nueva elegida silenciosamente. Reutiliza archivos sin cambios, descarga los
nuevos o modificados y verifica el conjunto antes de activarlo. Si falla, conserva la
instalación anterior; nunca publica una mezcla de shards de revisiones distintas.
Los casos que requieren intervención se informan sin sustituir sus archivos.
Sin nombre, aplica los planes de actualización resueltos de todos los gestionados del
catálogo, incluidos companions. No sustituye update ni selecciona una revisión nueva.
Informa unidades sin cambios, externas, ambiguas o fallidas sin impedir tratar las demás.
`model companion upgrade` sin nombre limita la operación a companions. No es una
transacción global: cada unidad se publica completa o conserva su instalación anterior.

La reutilización por archivo es el criterio inicial. La transferencia diferencial por
bloques es una optimización posterior, no un requisito para detectar actualizaciones.
Update puede consultar novedades aunque la unidad esté en uso. Upgrade de modelos o
companions se rechaza antes de descargar mientras una instancia activa utilice esa
unidad, indicando sus IDs; primero deben detenerse. Incluye planes de sólo renombre.
No se mantienen dos instalaciones de pesos para servir simultáneamente revisiones
distintas. Esto no elimina el espacio temporal de preparación necesario para verificar
la actualización y conservar la instalación anterior si falla. Coordinar inicio y
upgrade para impedir que una instancia adquiera los archivos durante su sustitución.

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
Se realiza con `model verify <nombre> --set-baseline` o su equivalente bajo
`model companion`. Calcula SHA-256 completos y registra la fecha; advierte el alcance
de esa base. El flag expresa creación o sustitución explícita de la referencia del
externo, no una verificación exitosa contra otra referencia. Lecturas fallidas o cambios
detectados durante la lectura no producen una base completa exitosa. En gestionados
se rechaza el flag: no se rebasan los hashes establecidos al descargar.

Seguimiento remoto e integridad tienen propósitos distintos, pero pueden compartir
algoritmo cuando corresponda. No se inventa un hash corto ni se confunden commit,
identificador Git, ETag y hash de los bytes. Verify nunca establece una base por omisión.

## Límites del catálogo

- No reconstruir obligatoriamente la procedencia de externos ni recorrer historiales exhaustivos.
- No escribir sidecars por archivo o shard.
- No descargar ni reconciliar companions automáticamente.
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
