# Modelos y companions: estados

Los diagramas están en [estados.md](estados.md).

## La regla

> **Cada pregunta es un eje propio. Los ejes no se combinan en un único estado.**
> Se muestran como columnas separadas, y cada uno aparece recién en la iteración que lo
> necesita.

| Eje | Pregunta | Iteración | Costo de responderla |
|---|---|---|---|
| **Origen** | ¿Gestionado (en el directorio de modelos) o externo? | núcleo | ninguno: es un atributo del registro, no un estado |
| **Disponibilidad** | ¿Se puede cargar ahora? | núcleo | barato: existencia, shards, encabezado |
| **Integridad** | ¿Los bytes cambiaron desde que se registraron? | biblioteca | caro: hash de gigabytes, sólo a pedido |
| **Procedencia** | ¿Coincide con lo publicado en el repositorio remoto? | biblioteca | red |

En el núcleo, **el único estado de un modelo o companion es su disponibilidad**, y se
deriva cada vez que se consulta; no se persiste.

Un modelo puede ocupar **uno o varios archivos GGUF**. Sus shards forman una unidad:
registrar, comprobar disponibilidad y eliminar se refieren al conjunto completo, no a
cada archivo por separado. Un companion es otra unidad y puede compartirse entre
modelos o cuantizaciones. La identidad remota de esas unidades queda pendiente para
la etapa específica de gestión de modelos.

## Disponibilidad, en el núcleo

- **Disponible:** la ruta existe, es legible, el encabezado GGUF se lee y, si es un
  conjunto de shards, están todos.
- **Incompleto:** faltan shards.
- **Inaccesible:** la ruta no existe o no se puede leer (disco desmontado, archivo
  movido, permisos).
- **Inválido:** el archivo existe pero no es un GGUF legible.

Ninguno de los cuatro exige hashear. Legacy necesitaba persistir `altered` justamente
porque derivarlo costaba hashear cada modelo en cada listado; en el núcleo esa pregunta
no se hace.

**Cambio detectado (aviso, no estado):** si el tamaño o la fecha de modificación difieren
de lo registrado, Coquena avisa y no bloquea. La consulta no actualiza el registro ni
afirma que los bytes sigan íntegros. El registro se actualiza sólo cuando el usuario
lo pide, con `model refresh`, que relee los metadatos necesarios y anota el tamaño y la
fecha actuales.

Es la misma separación que `git fetch` / `git pull` o `apt update` / `apt upgrade`:
observar y avisar es barato y automático; cambiar lo registrado es explícito. En la
iteración de la biblioteca el par se repite contra el repositorio remoto (`update`
observa, `upgrade` reemplaza bytes).

## Quitar un modelo o companion

`model remove` quita la unidad del registro. Si Coquena descargó sus archivos y los
guarda en su área gestionada, **borra físicamente todos los archivos propios** e informa
cuántos bytes liberó. Si la unidad es externa, **no toca sus archivos** e informa que
permanecen en disco; así no afecta a LM Studio ni a otra herramienta que los gestione.
La misma regla de propiedad rige para backends. Los presets que referencian una unidad
retirada no se borran: pasan a **No servible** por *referencia rota*.

Un **índice central en el área de modelos gestionados** es la alternativa preferida a
los sidecars que legacy escribió por archivo. Su ubicación exacta respecto del home
configurable, su formato y su recuperación se decidirán con la identidad de modelos;
no se crearán sidecars por shard por inercia de `legacy-go`. También quedan pendientes
las reglas de propiedad cuando varios modelos comparten un archivo y la recuperación
ante una eliminación parcial.

## Companions

Un companion tiene **exactamente los mismos estados** que un modelo. Lo propio del
companion no es su estado sino su relación con los modelos:

- **No depende de un modelo principal para estar disponible.** Un proyector sin su
  modelo instalado sigue disponible (misma conclusión que legacy).
- **La compatibilidad con un modelo se valida al asociarlo en un preset**, leyendo los
  metadatos GGUF de ambos (p. ej. un `mmproj` debe corresponder a la arquitectura del
  modelo). No es un estado del companion. Si la validación falla, la asociación se
  rechaza, salvo que el usuario la fuerce explícitamente (`--force`).
- **Un companion puede servir a varios modelos**, y un modelo puede tener varios
  companions (proyector, borrador).

## Preset: servible o no

El estado que realmente importa al lanzar no es el del modelo sino el del **preset**, y se
deriva de la disponibilidad de sus archivos:

- **Servible:** su modelo y todos sus companions están disponibles.
- **No servible:** alguno no está disponible, o la referencia está rota porque se quitó
  del registro. Al iniciar, el preset se omite con aviso; el servidor arranca si queda al
  menos uno servible.

## Lo que queda para la iteración de la biblioteca

Integridad y procedencia vuelven **como ejes separados**, cada uno con su propia columna,
sus propios valores y su propio comando, y nunca como valores adicionales de la
disponibilidad. Los hallazgos de legacy sobre Hugging Face (ETag, blob id, Xet, historial
destruible) se revisarán entonces, bajo demanda.

El registro futuro debe distinguir al menos dos **propósitos de evidencia**: comprobar
la integridad de los bytes locales desde su incorporación y comparar esos bytes con una
identidad de contenido publicada por el origen, cuando exista. No se presupone que un
hash corto sea comparable con Hugging Face ni que ambos propósitos requieran algoritmos
distintos. La comparación remota debe ser fehaciente, no una coincidencia heurística;
el cálculo de integridad completo puede ser una operación costosa y explícita. El
algoritmo, dominio, costo y persistencia de cada evidencia se decidirán en esa etapa.
