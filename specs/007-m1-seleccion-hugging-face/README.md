---
status: archived
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:16.165707400Z
updated_at: 2026-10-28T10:21:41.631098700Z
completed_at: 2026-10-08T13:29:20.164974700Z
transitions:
- status: in-progress
  at: 2026-10-08T13:17:08.906384500Z
- status: complete
  at: 2026-10-08T13:29:20.164974700Z
- status: archived
  at: 2026-10-28T10:21:41.631098700Z
---
# Selección Hugging Face y menú de descarga

## Objetivo
Adaptador de metadatos públicos: resolver commit, inventario paginado, variantes y unidad completa de shards. Implementar menú según cuantización indicada o ausente, README bajo demanda y cancelación. Producir selección fijada para el consumidor, sin descargar pesos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [x] Fixtures prueban paginación, revisión inexistente, rate limit, variantes ambiguas/shard faltante y README del mismo commit. Cancelar no crea GUID; elegir variante inicia sin segunda confirmación. No clonar ni recorrer historial; verificar API oficial vigente al implementar.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación del 8 de octubre de 2026, .NET SDK 10.0.401, Windows x64.
Sin paquetes nuevos, sin consultar legacy y sin commits.

### Definición confirmada por el usuario
La 007 entrega un servicio reutilizable que trata Hugging Face como un store de
repositorios/revisiones/archivos. Las tareas posteriores, en particular 009, consumen
esa selección y concretan la transferencia. Contemplar companions significa distinguir
sus unidades y permitir reutilizar el mismo acceso remoto; no agregar ahora su instalación,
comandos públicos ni un menú conjunto. La UX conserva lo que pide esta tarjeta y comandos.md.
No se cambiaron las reglas canónicas ni el alcance de M1.

### Implementación
- `src/Coquena/Models/HuggingFaceStore.cs`: ResolveAsync fija referencia a commit;
  InventoryAsync obtiene el árbol recursivo completo paginado; File consulta una ruta
  exacta de ese inventario; FileAddress construye una dirección /resolve/<commit>/<ruta>
  estable para el futuro consumidor; ReadmeAsync lee README del mismo commit a demanda.
  No hay clonado, consulta de historial, GET de pesos, creación de home/GUID ni persistencia.
- Acceso HTTP con HttpClient inyectable, JsonDocument y tipos propios, sin SDK Python/JS,
  framework de proveedores ni cambios en los contratos compartidos de 002.
  CreateClient desactiva cookies, redirecciones automáticas y descompresión.
- RepositoryFile conserva por separado GitOid, LfsSha256 y XetHash. ToRemoteFile produce
  identidad tipada para RemoteSelection: preferir LFS SHA-256/file-bytes, luego Xet/xet-file,
  finalmente SHA-1/git-blob. No interpretar Git ni Xet como SHA-256 de los pesos.
  No se calcula ningún hash ni se declara integridad verificada en esta tarjeta.
- Paginación mediante Link rel="next": host HTTPS exacto, mismo repositorio/commit,
  recursividad y forma preservadas; rechazar ciclo, enlaces inseguros, parámetros duplicados,
  entradas repetidas, JSON incoherente y páginas truncadas por límites. Nunca convertir
  un inventario incompleto o un fallo de acceso en ausencia confirmada de archivos.
- Límites técnicos: 4 MiB por respuesta JSON, 512 KiB para README, profundidad JSON 32,
  128 páginas/100000 entradas, 20 s por consulta y 60 s por inventario.
  Hasta tres intentos para HTTP 429/500/502/503/504. Respetar el mayor plazo de Retry-After
  y RateLimit t=; esperas mayores a 5 s se diagnostican sin reintentar antes del plazo.
  No hay reintento infinito ni autenticación; cancelación del llamador se conserva.
- Validar repositorio, commit, rutas relativas y nombres Windows inseguros/reservados.
  Conservar rutas exactas y escapar segmentos al construir direcciones. No persistir
  URLs firmadas, usar rutas locales ni seguir cambios de revisión mediante paginación.
- `src/Coquena/Models/ModelSelection.cs`: descubrimiento puro de variantes, por carpeta
  y prefijo exactos. Agrupar shards estándar -00001-of-00002.gguf, exigir 1..N y total
  consistente; conjuntos incompletos/contradictorios no son opciones de descarga.
  Conservar etiquetas completas, incluida IQ1_S-XL y MXFP4_MOE; dos variantes con la
  misma cuantización conservan opciones diferentes por sus rutas, sin adivinar.
- Reconocer candidatos auxiliares mmproj/mtp separados del principal, con cuantización
  independiente. No certificar compatibilidad ni asociarlos automáticamente; no ampliar
  M1 a instalación de companions. Nombres no reconocibles y partes no convencionales
  producen diagnóstico de limitación, no un juicio de invalidez del GGUF.
- ModelSelectionMenu presenta opciones Descargar <variante>, Mostrar README y Cancelar.
  Una cuantización indicada filtra, nunca sustituye automáticamente. Sin candidatos sólo
  README/Cancelar. README vuelve al mismo menú, también si falla; se obtiene sólo al pedirlo.
  Elegir devuelve exactamente una selección fijada sin segunda confirmación: el consumidor
  inicia la acción con ese resultado. Cancelar/EOF no produce selección, GUID ni parciales.
  IModelSelectionInteraction permite pruebas e integración; adaptador de consola disponible.
- No se agregó un comando ficticio ni una salida JSON pública. 009 conecta el menú al
  comando y motor de transferencia/publicación; 008 valida estructura GGUF real.

### API oficial y descubrimientos verificados
- [OpenAPI oficial vigente](https://huggingface.co/.well-known/openapi.json): tree admite
  recursive/expand/limit/cursor y devuelve Link para paginación; size representa los
  bytes del archivo LFS, no sólo el puntero. oid, lfs.oid y xetHash son campos distintos.
- [Cliente oficial](https://huggingface.co/docs/huggingface_hub/package_reference/hf_api):
  resolución por revisión, inventario y distinción de errores de revisión/acceso.
- [Rate limits oficiales](https://huggingface.co/docs/hub/rate-limits): HF informa reset
  en RateLimit t=, no sólo Retry-After. Fixture exige no ignorar el plazo más largo.
- [Cuantizaciones actuales de llama.cpp](https://github.com/ggml-org/llama.cpp/blob/master/tools/quantize/quantize.cpp):
  el recorrido real descubrió MXFP4_MOE; se verificó su nombre en código oficial y se
  incorporó al descubrimiento con fixture, sin suponer que cualquier GGUF es Q/IQ.
- Consulta HTTP manual con limit=2 comprobó Link rel=next en SmolVLM, sin pesos.

### Comandos reproducibles
```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
# Opt-in: sólo metadatos, README y selección, sin pesos.
& artifacts/tests/win-x64/Coquena.Tests.exe --hf-store ggml-org/SmolVLM-500M-Instruct-GGUF
& artifacts/tests/win-x64/Coquena.Tests.exe --hf-store unsloth/Qwen3.5-35B-A3B-GGUF
git diff --check
leanspec board
leanspec search hugging
leanspec view 007
```

### Resultados
- Runner administrado y runner Native AOT: 168 comprobaciones nuevas HF/selección OK.
  Fixtures cubren commit, identidades Git/LFS/Xet, paginación completa/ciclo/host o revisión
  cambiados, JSON inválido/duplicado, rutas inseguras, shard faltante/totales contradictorios,
  etiquetas actuales/no convencionales, variantes ambiguas, README del commit/no solicitado/
  fallido, cancelar/EOF/entrada inválida, ausencia de candidatos, interacción requerida,
  cancelación del token, 404 de revisión, 403, límites de tamaño, 429 con espera corta/larga
  y RateLimit t=, recuperación tras HTTP transitorio. Sin tráfico de pesos en las fixtures.
- Regresiones OK: nueve casos de CLI administrados y nueve del ejecutable publicado,
  contratos y persistencia, backends 180/181, transferencias 53/54 e instalación 52/56
  (administrado/nativo). CLI y runner publicados sin warnings/errores, AOT warnings-as-errors.
- Proveedor real con runner Native AOT, sólo metadatos/README/menú:
  SmolVLM-500M-Instruct-GGUF @ 72e986006ef53e37cdd3f6d4241c90b0f01df376: seis archivos,
  dos candidatos principales y dos mmproj; README 268 caracteres; selección f16 fijada.
  Qwen3.5-35B-A3B-GGUF @ bc014a17be43adabd7066b7a86075ff935c6a4e2: 32 archivos,
  24 variantes principales y tres mmproj; BF16 consta de dos shards ordenados; README
  95971 caracteres; selección BF16 fijada. MXFP4_MOE reconocido tras la verificación oficial.
  No se transfirieron pesos, crearon GUIDs ni instalaciones en ninguno de esos recorridos.
- Enlaces locales de la tarjeta y git diff --check OK. Sin bloques PlantUML/ejemplos JSON
  nuevos ni cambios en documentos canónicos; no corresponde declarar renderizado adicional.
  Alcance, dependencia 002 completa y evidencia revisados mediante board/search/view.

### Límites y siguiente paso
- Sin transferencia, archivos instalados, validación GGUF, SHA-256 calculado, publicación,
  catálogo de modelos, update/upgrade, autenticación, repositorios restringidos ni comandos
  companions públicos. No se presenta el menú como un model download ya disponible.
- Las etiquetas se descubren por convenciones de nombres, no inspeccionando pesos.
  Nombres arbitrarios no se completan por semejanza; nuevas convenciones necesitan
  evidencia y pruebas. Un candidato no certifica compatibilidad ni estructura/integridad.
- Repositorios que requieran redirecciones de metadatos se diagnostican; no hay seguimiento
  automático a otros hosts ni garantía de resolución de aliases/renombres remotos.
- Menú probado con interacción inyectada en runner administrado/nativo; no se afirma
  prueba física de teclado/Ctrl+C del menú. Límites de tiempo implementados, sin prueba
  de espera real de 20/60 s ni de rate limit real del proveedor; HTTP adverso con fixtures.
- Siguiente por orden: 008, inspección GGUF e integridad. 009 reutiliza este store y menú
  para concretar descarga/publicación, manteniendo los boundaries de cada tarjeta.
### Evaluación adicional solicitada: Ornith, Qwen y Gemma
Recorrido del 8 de octubre de 2026 usando el runner Native AOT existente --hf-store;
sin cambios en código ni transferencia de pesos. Resolución, inventario, README del
commit y selección de principal sin segunda confirmación pasaron en los tres repositorios.

| Repositorio | Commit | Archivos del inventario | Variantes principales | Companions seleccionables |
|---|---|---|---|---|
| ornith-ai/Ornith-1.5-9B-GGUF | abdd624b12ebf020b767fff532ff44fe552b28c3 | 10 | 5, un archivo por variante | mmproj BF16 |
| lmstudio-community/Qwen3.8-27B-GGUF | 5a7da681f60570ab5b439a587e912d2e5eddb582 | 6 | 3, un archivo por variante | mmproj BF16 |
| unsloth/gemma-4-31B-it-GGUF | c1ac76e99d5513b141e8adde7288b85c3f9c32ec | 35 | 22; BF16 dividido en dos shards | mmproj BF16/F16/F32; mtp BF16/F16/Q8_0 |

README: Ornith 26763 caracteres, Qwen 2100, Gemma 28465. Selecciones de prueba:
Ornith BF16, Qwen Q4_K_M y Gemma BF16 (dos shards). Son selecciones, no instalaciones.

Descubrimiento relevante de Gemma: mtp-gemma-4-31B-it.gguf no lleva cuantización en su
nombre y se diagnostica/no ofrece automáticamente. El inventario fijado declara el mismo
LFS SHA-256 y tamaño (514687104 bytes) que MTP/mtp-gemma-4-31B-it-Q8_0.gguf. Esa ruta
etiquetada sí está contemplada y se ofrece como mtp:Q8_0. No se dedujo cuantización del
archivo sin etiqueta, no se creó un alias ni se certificaron los bytes descargados.
También existe MTP/README.md: el store conserva esa ruta en el inventario; el menú acordado
sigue consultando únicamente README.md raíz. Lectura genérica de documentos auxiliares
no se implementa por omisión ni se cambia la UX de la tarjeta.

```powershell
& artifacts/tests/win-x64/Coquena.Tests.exe --hf-store ornith-ai/Ornith-1.5-9B-GGUF
& artifacts/tests/win-x64/Coquena.Tests.exe --hf-store lmstudio-community/Qwen3.8-27B-GGUF
& artifacts/tests/win-x64/Coquena.Tests.exe --hf-store unsloth/gemma-4-31B-it-GGUF
```

### Aceptación
El usuario confirmó que todo es correcto, dio por terminada la tarea 007 y autorizó
el commit conjunto de implementación, pruebas y evidencia, incluida la evaluación
de los tres repositorios adicionales. No se amplía el alcance a 008/009 ni companions
instalados.
