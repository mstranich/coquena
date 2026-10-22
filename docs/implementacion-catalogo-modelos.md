# Implementación del catálogo de modelos

## Contrato de trabajo para agentes

Esta guía relaciona invariantes técnicas con los documentos funcionales.
No define entregas ni autoriza implementar capacidades adicionales.
Cada catálogo pertenece a un ámbito de proyecto o global importado; no inferirlo a partir
de nombres de clases o formatos históricos. Mantener C# y Native AOT verificables.

## 1. Separación mínima de responsabilidades

Separar resolución de YAML/overrides/variables, validación, resolución de recursos,
transferencia, publicación de locks/inventario y operación/observación del proceso.
CLI y frontends invocan esas mismas operaciones, sin duplicar políticas.

El adaptador de argumentos no implica adaptador de APIs: un fork puede necesitar
otro healthcheck y carecer de recarga, métricas o eventos del oficial.
La capacidad se demuestra sobre el build concreto, no por el nombre del repositorio.

## 2. Datos mínimos y significado

Conservar por separado:
- Intención efectiva, archivos de origen y orden.
- Resolución portable de origen/revisión/artefactos y referencias de integridad.
- Instalaciones físicas y propiedad del inventario local.
- Plan update fijado a intención/resolución de partida.
- Operaciones pendientes y sus checkpoints.
- Instancia identificada, composición activa y última buena local.
- Observaciones de integridad por recurso y fecha.
- Borradores por sesión y versión original del archivo.

Identidad de contenido exige algoritmo, dominio y valor completo. Commit, Git blob,
ETag y SHA-256 de archivo no son equivalentes. Una igualdad sin algoritmo comparable
no prueba igualdad de bytes. No inventar hashes abreviados.

Las referencias lógicas sobreviven renombres y revisiones físicas.
El nombre canónico del inventario no es el alias del router ni una ruta.
Bind conserva identidad de recurso; set conserva un literal.

### Almacenamiento local acordado

Los recursos se guardan en su ámbito de preparación. .coquena separa datos compartibles
de estado local ignorado por Git; layout y cantidad de locks no se fijan aquí.
Generados del router, preparación e inventario no se guardan junto a externos.
Home conserva registro/prefs y el ámbito importado; el catálogo global no es obligatorio.

Compartir un lock no certifica que funcionó en otra máquina.
La última buena y la composición activa se registran localmente.
No incorporar valores secretos expandidos a locks, planes o borradores compartidos.

## 3. Registro externo y comprobaciones locales

Registrar rutas y cabeceras con lecturas acotadas, conservando todas las partes
esperadas de shards. Un externo incompleto se diagnostica sin copiar/reparar bytes.
Resolver el alta contra la base del proyecto. Si el usuario copió el archivo,
registrar la copia y no su origen histórico. Separar ubicación relativa/absoluta,
propiedad, disponibilidad y correspondencia remota; no deducir ninguna de las
otras por encontrarse el archivo dentro de la carpeta.
--try-match se ejecuta sólo por elección y después de persistir el alta.
Un fallo remoto no revierte el registro ni habilita gestión de sus archivos.

Disponibilidad no calcula hashes completos. Verify recorre archivos con memoria
acotada, compara referencias y persiste resultados por recurso.
Ausencia de referencia es no concluyente; crear una base externa es otra operación
explícita. No actualizar la referencia con el hash de un archivo que falló.
Si cambia durante la lectura, no publicar un éxito concluyente.

## 4. Adaptador de Hugging Face: sólo la información necesaria

Operaciones requeridas, independientemente de la librería elegida:

| Operación | Resultado requerido |
|---|---|
| Resolver referencia | Commit concreto del repositorio |
| Consultar inventario en ese commit | Rutas, tamaños e identidades de contenido tipadas, con paginación completa |
| Consultar metadatos de archivo | Identidad y ubicación de descarga, cuando el inventario no alcance |
| Descargar selección fijada | Flujo de bytes de cada archivo elegido en ese commit |

No clonar Git, descargar tensores para consultar novedades ni inspeccionar todos los
GGUF del Hub. Empezar por las rutas seleccionadas; ampliar al inventario del repositorio
si hace falta localizar una reubicación. La paginación incompleta no prueba ausencia.

Autenticación, red, límites de peticiones, repositorio inaccesible y revisión inexistente
se informan como fallos de consulta, no como actualización ni borrado remoto confirmado.
Respetar las indicaciones de espera del servidor y usar reintentos acotados; no sondear
en bucle ni efectuar búsquedas históricas exhaustivas. Nunca registrar credenciales o
URLs firmadas como identidad duradera. No reenviar credenciales a cualquier host al
seguir redirecciones.

Antes de elegir endpoints/librería, comprobar estas capacidades con fixtures pequeños
y documentación oficial. No hacer experimentos descargando modelos de gigabytes.
La sintaxis de la API Python citada como referencia no exige introducir Python en C#.

### Inspección acotada de GGUF

`ModelSelection` conserva clasificación/agrupación pura y evidencia por archivo:
commit/ruta, rol, base y etiqueta completa, fuente, estado y diagnóstico. `ModelResolver`
inspecciona nombres sin etiqueta y valida todas las partes finalmente elegidas.
`RemoteGgufInspector` es async, secuencial e inyectable; comparte el parser binario con
la inspección local. La API agregada `gguf` de HF no aporta cuantización por ruta y no
se aplica al inventario como si lo hiciera. No se inspeccionan descriptores ni se deduce
la receta K_M/K_S de tipos de tensores. Tabla de `llama_ftype` fijada a
[llama.h 71ad059](https://github.com/ggml-org/llama.cpp/blob/71ad0590f4808b6202f9213d166913858c73b1bc/include/llama.h),
incluidos BF16/IQ/TQ/MXFP/NVFP y Q1_0/Q2_0; IDs retirados/futuros permanecen desconocidos.

Etiquetas dinámicas: [documentación del publicador](https://unsloth.ai/docs/models/qwen3.5/gguf-benchmarks)
y rutas del snapshot Qwen bc014a1 respaldan `UD-IQ2_M`, `UD-IQ2_XXS`, `UD-IQ3_S`,
`UD-IQ3_XXS`, `UD-IQ4_NL`, `UD-IQ4_XS` y `UD-Q2_K_XL`, `UD-Q3_K_XL`, `UD-Q4_K_L`,
`UD-Q4_K_XL`, `UD-Q5_K_XL`, `UD-Q6_K_S`, `UD-Q6_K_XL`, `UD-Q8_K_XL`.
La tabla de nombres conserva estas etiquetas exactas; no acepta cualquier sufijo.
La cabecera no certifica una receta dinámica ni la reduce a la etiqueta estándar
predominante. No se ofrece una equivalencia/alias implícita para omitir `UD-`.

La lectura usa `resolve/<commit>/<ruta>` y `ResponseHeadersRead`, rangos cerrados,
`Accept-Encoding: identity`, sin cookies/redirecciones automáticas. Utiliza una política
HF/CDN restringida (cinco saltos, HTTPS, hosts permitidos, sin credenciales). Exige 206,
Content-Range exacto y total de inventario, longitudes/cuerpo coherentes, sin compresión;
contrasta commit opcional, continuidad de ETag e identidad LFS comparable cuando existe.
Un 200 se cierra sin drenar; 416/rangos falsos/cambio de contenido son fallos de consulta.

Presupuestos: primer bloque 64 KiB, siguientes hasta 1 MiB; máximo 64 MiB por cabecera,
128 MiB por unidad, 256 MiB y 8192 peticiones (incluidos redirects) por invocación;
20 segundos por petición, 120 por sesión. Memoria de cuerpo acotada a dos bloques y
scratch del parser de 64 KiB, sin conservar tokenizer ni pesos. Contadores incluyen
bytes leídos en fallos y un byte de detección de exceso. Máximo tres intentos HTTP
429/500/502/503/504; Retry-After y RateLimit toman la espera mayor, más de 5 s se informa
sin reintentar antes del plazo. Cancelación y fallos no se convierten en ausencia.
Memoización por repo/commit/ruta sólo durante la invocación; sin caché en disco, home,
GUID ni `.part`. Los límites del inventario siguen siendo independientes.

Los límites de inspección no sustituyen la verificación completa de una descarga.
No ampliar búsquedas o presupuestos silenciosamente para convertir un resultado
no concluyente en una selección aparentemente segura.


## 5. Comparación determinista y update

Las [reglas de correspondencia](modelos-y-companions.md#reglas-de-correspondencia)
son deterministas sobre instalación y metadatos remotos. Sin identidad comparable,
resultado no concluyente; no recurrir a semejanza opaca ni cambiar cuantización.
Inventario remoto incompleto no demuestra desaparición.

Update guarda plan actual/propuesto, selección concreta y huella de intención.
No descarga ni cambia YAML. Un cambio de intención invalida el plan.
La operación comprende la combinación elegida, no todos los recursos de la máquina.
Seleccionar otra variante explícita no equivale a descubrir automáticamente una sucesora.

## 6. Descarga inicial y `upgrade`: publicación recuperable

La descarga sigue [descargas](descargas.md). Upgrade consume exactamente el plan
y prepara/verifica todas las unidades seleccionadas antes de publicar resoluciones.
Un filtro delimita ese conjunto; no convierte un fallo dentro de él en éxito parcial.
Up puede preparar una intención cambiada sin renovar latest no modificado.

Las revisiones físicas nuevas son independientes de las activas. Conservar anteriores
y última buena; preparar con servidor activo no autoriza reemplazar sus archivos.
No suponer transacción atómica de sistema de archivos sobre todos los documentos:
usar un protocolo recuperable con comprobación de generación y propiedad.
Prever interrupción antes/después de publicar y limpieza parcialmente fallida.

La transición externo→gestionado exige selección explícita y publicación verificada,
sin adoptar, mover ni borrar los externos. Coordinar referencias durante transición;
no cambiar bajo una instancia las rutas de recursos que ya utiliza.

## 7. Emparejamiento

El matching externo es informativo: metadatos, rol, cuantización, partes y tamaños,
con selección explícita. No prueba revisión ni integridad y no habilita update.
Se aplican los límites de [modelos](modelos-y-companions.md#incorporación-externo-o-gestionado).
Nunca descargar pesos sólo para reconstruir procedencia.

## 8. Evidencia de aceptación técnica

La validación debe cubrir coherencia de combinaciones/locks, concurrencia de escritura,
fallos a mitad de descarga/publicación, recuperación y propiedad.
Probar archivos pequeños/fixtures para límites de red, rangos y parsing.
Los recorridos de runtime requieren binario publicado Native AOT; una prueba
administrada no demuestra que una dependencia funcione publicada.

Para router, probar sobre build fijado: inicio vacío, publicación/recarga de presets,
carga/descarga por entrada, errores de modelo, eventos y recuperación.
No asumir que éxito HTTP equivale a carga completada ni que existe una transacción
nativa para aplicar varios presets.

## 9. Decisiones que no deben quedar ocultas en el código

El [formato](formato-proyecto.md) define YAML sin campo de versión y referencias. [Parámetros](catalogo-de-parametros.md) fija metaschema y emisión; [persistencia](persistencia-proyectos.md) concreta documentos, identidad y
publicación recuperable con control de escritores. Capacidades se acreditan por build.
Las invariantes aprobadas no autorizan elegir por silencio una política distinta.

La [restauración acotada](servidores.md#restauración-acotada-de-presets) conserva entradas no afectadas y exige reinicio explícito si restaurar lo requiere. La evidencia del build determina capacidades reales, sin redefinir esa política ni afirmar soporte probado.

## Referencias

- [Reglas de modelos y companions](modelos-y-companions.md).
- [Proyecto, composición y locks](definiciones-declarativas.md).
- [Preparación, upgrade y limpieza](actualizacion-y-limpieza.md).
- [Observación de APIs y procesos](observabilidad.md).
