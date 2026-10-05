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

## Disponibilidad, en el núcleo

- **Disponible:** la ruta existe, es legible, el encabezado GGUF se lee y, si es un
  conjunto de shards, están todos.
- **Incompleto:** faltan shards.
- **Inaccesible:** la ruta no existe o no se puede leer (disco desmontado, archivo
  movido, permisos).
- **Inválido:** el archivo existe pero no es un GGUF legible.

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
varias variantes; su selección explícita y representación local deben desambiguarse.

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
inconsistentes. El protocolo físico concreto, su relación con el índice y modelos en uso
se define al cerrar almacenamiento y publicación recuperable.

## Quitar un modelo o companion

`model remove` y `model companion remove` quitan la unidad correspondiente del registro.
Si Coquena descargó sus archivos y los
guarda en su área gestionada, **borra físicamente todos los archivos propios** e informa
cuántos bytes liberó. Si la unidad es externa, **no toca sus archivos** e informa que
permanecen en disco; así no afecta a LM Studio ni a otra herramienta que los gestione.
La misma regla de propiedad rige para backends. Los presets que referencian una unidad
retirada no se borran: pasan a **No servible** por *referencia rota*.

El catálogo central es `<home>/catalogs/models.json`, sin sidecars por archivo o shard.
Los pesos siguen `models/<usuario>/<repositorio>/<ruta-remota>`, sin carpetas propias
por cuantización. La estructura completa está en la guía técnica. Recuperación del índice,
propiedad de archivos compartidos y eliminaciones parciales siguen pendientes.

## Companions

Un companion tiene **exactamente los mismos estados** que un modelo. Lo propio del
companion no es su estado sino su relación con los modelos:

- **No depende de un modelo principal para estar disponible.** Un proyector sin su
  modelo instalado sigue disponible.
- **Registro:** `model companion add <ruta.gguf> --name <usuario/repositorio#tipo:cuantización> [--try-match]`.
  El tipo está en el nombre, sin `--type`; no configura un preset.
- **Retiro:** `model companion remove <nombre>` resuelve el registro por ese alias.
- **Uso:** un parámetro de configuración referencia al companion registrado. El preset
  puede heredar esa referencia o sobrescribirla; Coquena la resuelve a una ruta absoluta
  en el INI. No hay una segunda lista de asociaciones en el preset.
- **Compatibilidad funcional:** queda a cargo del backend al cargar la combinación.
  Coquena no la certifica ni bloquea la configuración por esa causa. No hay un modo
  forzado: registrar y configurar los archivos basta para intentar utilizarlos.
- **Un companion puede servir a varios modelos**, y un modelo puede tener varios
  companions (proyector, borrador).

## Preset: servible o no

El estado que realmente importa al lanzar no es el del modelo sino el del **preset**, y se
deriva de la disponibilidad de sus archivos:

- **Servible:** su modelo y los companions referenciados por su configuración efectiva
  están disponibles. No garantiza que el backend consiga cargarlos.
- **No servible:** alguno no está disponible, o la referencia está rota porque se quitó
  del registro. La decisión de omitirlo o rechazar el lanzamiento queda para servidores.

## Incorporación: externo o gestionado

Registrar un externo no requiere identificar su revisión remota ni calcular un hash
completo. Basta la referencia del catálogo a sus rutas: no necesita symlinks ni hardlinks.
Si otra herramienta mueve, elimina o reemplaza esos archivos, Coquena informa lo que
observa; no los repara, actualiza ni adopta automáticamente.

El alta exige `--name <nombre-canónico>`: declaración del usuario, no prueba de procedencia.
Sin `--try-match`, el registro es exclusivamente local. `--try-match` es un flag opcional
sin valor: intenta emparejar la selección en el repositorio declarado usando metadatos
locales. Fallo de red, ausencia de coincidencias o ambigüedad no invalidan el registro
ni cambian su nombre. Confirmar y persistir candidatos sigue pendiente.
No reconstruye procedencia exhaustivamente ni habilita update/upgrade del externo.
Una descarga gestionada posterior deja intactos los archivos externos.

## Seguimiento y actualización de gestionados

El registro conserva el repositorio, la referencia seguida (normalmente una rama como
`main`), la selección de archivos, el commit de descarga y, por archivo, rutas, tamaño
e identidad de contenido con su tipo. Conserva por separado el resultado y momento de
la última consulta y la revisión candidata. No confunde revisión instalada con consultada.

Se sigue el repositorio que publica los archivos seleccionados: actualizar el modelo
original no implica que un tercero haya regenerado su GGUF. El nombre local no cambia
al actualizar los pesos.

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
Un externo explícito informa que no aplica. El alcance sin nombre queda por precisar.

Una consulta fallida significa **no se pudo comprobar**; una correspondencia ambigua,
**requiere intervención**. Ninguna equivale a «actualizado» ni necesariamente a
«desactualizado». Se conserva la última observación con su fecha, sin presentarla como
una comprobación actual.

`model upgrade [nombre]` aplica la revisión concreta detectada por `update`, no una
revisión nueva elegida silenciosamente. Reutiliza archivos sin cambios, descarga los
nuevos o modificados y verifica el conjunto antes de activarlo. Si falla, conserva la
instalación anterior; nunca publica una mezcla de shards de revisiones distintas.
Los casos que requieren intervención se informan sin sustituir sus archivos.
El alcance sin nombre queda por precisar.

La reutilización por archivo es el criterio inicial. La transferencia diferencial por
bloques es una optimización posterior, no un requisito para detectar actualizaciones.
Quedan pendientes las reglas de aplicación con modelos en uso, para la revisión de
servidores e instanciación.

## Integridad local

Al descargar se calcula y registra evidencia de integridad y se contrasta con la
identidad de contenido remoto cuando sea comparable. La comprobación posterior lee los
archivos completos y compara contra esa referencia. Registra el resultado y su fecha:
no certifica indefinidamente que el disco sigue intacto.

Una discrepancia se informa **sin sustituir el hash de referencia por el del archivo
alterado**. `refresh` no restablece esa referencia ni declara integridad.

En externos, establecer una referencia de integridad es opcional y explícito. Sólo
permite afirmar después «no cambió desde aquella lectura», no que la descarga original
fuera correcta o coincidiera con Hugging Face. No es requisito para registrarlos.

Seguimiento remoto e integridad tienen propósitos distintos, pero pueden compartir
algoritmo cuando corresponda. No se inventa un hash corto ni se confunden commit,
identificador Git, ETag y hash de los bytes. La elección técnica y la superficie del
comando de verificación quedan pendientes.

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
