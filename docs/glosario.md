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

Coquena usa una única convención para tamaños de archivos, transferencias,
almacenamiento, RAM, VRAM, límites y tasas de bytes: B, KB, MB, GB, TB, PB y EB.
Cada magnitud equivale a 1024 de la anterior: 1 KB = 1024 B, 1 MB = 1024 KB,
1 GB = 1024 MB y 1 TB = 1024 GB. Estas etiquetas representan siempre esa escala
binaria dentro de Coquena. No se muestra una segunda conversión decimal ni otra
familia de abreviaturas para el mismo valor.

La salida dirigida a personas elige automáticamente la unidad adecuada, subiendo
de magnitud al alcanzar 1024. Usa como máximo dos decimales, redondea para mostrar
y elimina ceros decimales innecesarios: `64 KB`, `1,5 GB`, `2 TB`. Si el redondeo
alcanza 1024, se pasa a la unidad siguiente. No se añade entre paréntesis la cifra
completa en bytes ni se ofrece una segunda representación del mismo tamaño.
Esta regla se aplica a CLI, tablas, progreso, mensajes, logs de Coquena y pantallas,
incluidos tamaños totales, espacio libre, memoria y tasas como `12,5 MB/s`.
La precisión de presentación nunca altera cantidades, límites o comparaciones.

La entrada de tamaños con esos sufijos sigue la misma escala que la salida;
B o un entero sin sufijo expresa bytes. Las cantidades persistidas y recibidas de
APIs se conservan en bytes. El formato visible se calcula a partir de esos bytes,
sin interpretar como binaria una cifra decimal ya formateada por un proveedor.
Los documentos de estado y respuestas estructuradas conservan cantidades exactas
para procesamiento; no se vuelcan como cifras sin formato en una vista para personas.
Las tasas usan las mismas unidades por segundo; bits, tokens y tiempos conservan
sus propias unidades. El cambio de etiqueta no modifica límites ni bytes medidos.
