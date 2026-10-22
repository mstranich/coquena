# Glosario

| Término | Significado |
| --- | --- |
| Proyecto | Carpeta con receta de servicio, consumidor o ambos, recursos y estado propio. |
| Estado de receta | Registro local en .coquena/ del nombre de receta usado y entidades creadas; permite reconocer nombres anteriores tras renombrar la carpeta. |
| Instancia | Proceso en ejecución de un servicio; máximo uno por identidad de servicio. |
| Home | Preferencias, registro y ámbito global importado; no almacén obligatorio para proyectos. |
| Backend | Ejecutable/runtime, versión, variante y artefactos necesarios para servir. |
| Modelo | Unidad de pesos principal, posiblemente dividida en shards. |
| Companion | Recurso auxiliar independiente, como proyector o modelo de especulación. |
| Preset | Definición nombrada de modelo, companions y argumentos; puede publicarse como entrada del router. |
| Combinación | Archivos YAML seleccionados y su orden; produce una intención efectiva. |
| Override | Capa que modifica una intención anterior; omitir conserva, valor vacío desasigna. |
| Lock | Resolución transportable de intención a versiones y archivos concretos. |
| Inventario local | Registro de instalaciones, propiedad y observaciones de un ámbito global o de proyecto. |
| Composición activa | Snapshot local de lo que ejecuta la instancia, distinto de YAML o borrador. |
| Última buena | Configuración local comprobada mediante salud y elegible para recuperación. |
| Borrador | Edición de una sesión, recuperable localmente sin escribir el YAML definitivo. |
| Gestionado | Recurso cuyos archivos propios descarga y administra Coquena. |
| Externo | Archivo no adquirido bajo gestión de Coquena, incluso si el usuario lo copió dentro del proyecto; registrarlo no autoriza moverlo, actualizarlo ni borrarlo. |
| Explícito | Recurso descargado/manualmente protegido contra limpieza automática. |
| Automático | Recurso obtenido para satisfacer intención/upgrade; candidato a limpieza cuando pierde protecciones. |
| Disponibilidad | Existencia, legibilidad y estructura; no demuestra integridad o carga exitosa. |
| Integridad | Comparación completa de bytes contra referencia conocida. |
| Seguimiento | Comparación con el origen remoto; distinto de integridad. |
| Apply | Aplicación explícita de presets compatibles a la instancia; no simple guardado. |
| Salud | Comprobación de que el servidor/router atiende; no prueba de todos los modelos. |
| Observador | CLI watch, TUI o Web UI conectado mientras está abierto; no supervisor residente. |
| Schema | Definición optativa de argumentos, tipos, reglas y presentación del proyecto. |
| Metaschema | Contrato para escribir y validar esos schemas. |

## Unidades de tamaño y memoria

La presentación de tamaños, memoria, límites y tasas de bytes usa exclusivamente
B, KiB, MiB, GiB, TiB, PiB y EiB, con escala 1024; las tasas se expresan por segundo.

La entrada no distingue mayúsculas de minúsculas y acepta:

- `B` o un entero sin sufijo para bytes.
- `KiB`, `MiB`, `GiB`, `TiB`, `PiB` y `EiB` para múltiplos de 1024.
- `KB`, `MB`, `GB`, `TB`, `PB` y `EB` como alias de esas unidades binarias,
  por usos y costumbres: `1MB` equivale a `1MiB`, no a un millón de bytes.
- `k`, `m`, `g`, `t`, `p` y `e`, sin `B`, para múltiplos de 1000.

Por ejemplo, `1k` y `1K` representan 1000 bytes; `1kb`, `1KB` y `1KiB`
representan 1024 bytes. `1m` representa 1.000.000 bytes, mientras que `1MB`
representa 1.048.576 bytes. Con sufijo se admiten decimales con punto siempre
que representen bytes enteros; por ejemplo, `1.5MB` equivale a `1.5MiB`.

En todas las interfaces humanas, la unidad se elige automáticamente y se muestran
hasta dos decimales sin ceros innecesarios, por ejemplo `1,5 GiB`. Si el redondeo
alcanza 1024, se pasa a la unidad siguiente. No se duplica el valor en bytes ni en
otra escala.

La persistencia, las APIs y las respuestas estructuradas conservan cantidades
exactas en bytes; el formato visible no altera valores, límites ni comparaciones.
Las cifras ya formateadas por proveedores no se reinterpretan. Bits, tokens y
tiempos conservan sus propias unidades.
