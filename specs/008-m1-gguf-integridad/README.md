---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:17.123689500Z
updated_at: 2026-10-08T15:16:35.703739Z
completed_at: 2026-10-08T15:16:35.703739Z
transitions:
- status: in-progress
  at: 2026-10-08T14:34:17.570569400Z
- status: complete
  at: 2026-10-08T15:16:35.703739Z
---
# Inspección GGUF y coherencia de shards

## Objetivo
Implementar lectura acotada de encabezados y agrupación estándar, completitud y coherencia estructural del conjunto. Proveer SHA-256 de bytes completo y resultado de integridad, separados de identidad/revisión remota.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [x] Fixtures de un archivo y múltiples shards: faltantes, duplicados, encabezado truncado, metadatos contradictorios y digest incorrecto. Hash incremental con memoria acotada y prefijo reanudado cubierto. No certificar compatibilidad de inferencia ni tratar commit/ETag como SHA de pesos.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación del 8 de octubre de 2026, .NET SDK 10.0.401, Windows x64.
Sin paquetes nuevos, sin cambios en contratos compartidos ni comandos públicos, sin
consultar legacy. Implementación y evidencia aceptadas por el usuario.

### Alcance implementado
- `src/Coquena/Models/GgufInspection.cs`: lectura de encabezado y metadatos GGUF v2/v3
  little-endian, sin cargar pesos ni descriptores de tensores. No es un validador universal
  de GGUF ni certifica que llama.cpp pueda cargar el archivo.
- Inspección acotada a 64 MiB, hasta 1000000 claves/elementos por array, claves de 1024
  bytes, pistas retenidas de 65536 bytes, anidamiento hasta ocho niveles, scratch de 64 KiB.
  Conteos y longitudes se comprueban antes de asignar/leer; claves duplicadas, truncamiento,
  tipos split incorrectos y pistas retenidas con UTF-8 inválido se diagnostican.
  Metadatos no retenidos se recorren sin materializar arrays ni strings completos.
- Inspect recibe el primer shard y localiza sólo hermanos con nombres exactos en la misma
  carpeta. InspectPaths consume una selección explícita, ordena y conserva el conjunto
  esperado completo; no mezcla prefijos/carpetas/variantes ni acepta rutas duplicadas.
  No recorre carpetas recursivamente, repara externos ni agrupa por semejanza.
- Contrasta nombres 1..N contra split.no (base cero), split.count UINT16 y
  split.tensors.count INT32, versiones y pistas compartidas presentes. Comprueba la suma
  de cantidades de tensores de encabezados para conjuntos completos, no sus datos.
  Las pistas pueden faltar en shards posteriores: no exigir copiar metadatos del primero.
- Resultados distinguen complete, incomplete, invalid, unsupported y read-error;
  las partes esperadas, faltantes y diagnósticos permanecen visibles. Complete indica
  sólo que pasaron estas comprobaciones de encabezado/conjunto, nunca integridad de pesos.
  Versiones/endian/tipos desconocidos, límites excedidos y división no convencional
  producen limitación, no certificación de corrupción del formato.
- `src/Coquena/Models/FileIntegrity.cs`: SHA-256 incremental completo con buffer de 64 KiB.
  FileHashSession relee el prefijo existente y recibe los bytes nuevos mediante Append;
  no persiste estado criptográfico ni toma un hash del checkpoint como evidencia.
  El futuro consumidor 009 debe invocar Append para cada bloque escrito y descartar el
  resultado ante fallo/incompletitud. La fixture escribe esos mismos bloques y compara
  el resultado con el hash independiente del archivo completo en cinco offsets de reanudación.
- VerifyAsync recorre un archivo sin red y compara únicamente sha256/file-bytes completos.
  Distingue verified, mismatch, no-reference, read-error y changed-during-read. No crea,
  sustituye ni persiste una base por omisión. Git/commit/ETag/Xet no se convierten en SHA
  de pesos por longitud o apariencia. La referencia incorrecta permanece intacta.
  Cancelación se propaga, no se convierte en discrepancia.
- Apertura FileShare.Read excluye escritores habituales en Windows; tamaño/fecha se
  contrastan antes/después. No promete detectar toda alteración concurrente ni certificar
  indefinidamente el disco. No se probó una modificación que restaure tamaño/fecha.

### Evidencia oficial consultada
- [Formato GGUF](https://github.com/ggml-org/ggml/blob/master/docs/gguf.md): framing,
  tipos, arrays, versiones y convención de nombres; se implementa inspección de metadatos,
  no la validación/carga completa de tensores.
- [Cargador llama.cpp](https://github.com/ggml-org/llama.cpp/blob/master/src/llama-model-loader.cpp):
  split.no base cero, split.count UINT16 y cantidad global de tensores INT32.
- [gguf-split oficial](https://github.com/ggml-org/llama.cpp/blob/master/tools/gguf-split/gguf-split.cpp):
  sólo el primer shard conserva todos los metadatos; los siguientes pueden tener sólo
  claves split. Merge conserva las claves pero pone split.count=0 para desactivar división.
  Ambos casos se contemplan explícitamente con fixtures; cero no se rechaza como corrupción
  de un archivo único fusionado.

### Validación reproducible
```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
git diff --check
leanspec board
leanspec search gguf
leanspec view 008
```

### Resultados
- 127 comprobaciones nuevas GGUF/integridad en runner administrado y Native AOT.
  Un archivo de nombre arbitrario y shards completos/faltantes/duplicados, selección
  parcial/reordenada/mixta, índices/totales/pistas contradictorios, suma de tensores,
  encabezado truncado en cada posición, magic/version/endian, límites/tipos inválidos,
  arrays y claves duplicadas, archivo bloqueado, cancelación, hashes completos correctos
  e incorrectos, referencias no comparables/ausentes, error de lectura y prefijo reanudado.
  Se comprueba que la lectura de encabezado se detiene antes de los pesos/descriptores.
- Regresiones administradas/nativas OK: nueve casos CLI (también nueve del ejecutable
  publicado), contratos/persistencia, backends 180/181, transferencias 53/54,
  instalación 52/56 y HF/selección 168. CLI y runner Native AOT publicados con
  warnings-as-errors y ejecutados; no sólo compilación administrada.
- No se descargaron modelos ni se escribió en D:\Coquena. Fixtures pequeñas en directorios
  temporales exclusivos; sin tráfico remoto de pesos. Consulta remota limitada a fuentes
  oficiales para verificar el formato y comportamiento de split/merge.
- No se cambiaron documentos canónicos, PlantUML ni ejemplos JSON; revisión de enlaces
  locales y git diff --check. Dependencia 002 completa y alcance revisado con board/search/view.

### Límites y siguiente paso
- No hay model verify público, alta externa, baseline persistido, catálogo, descarga o
  publicación de modelos: 009 integra los servicios con selección/transferencia; los
  comandos externos/verify y companions públicos pertenecen a M2.
- No se valida tamaño/layout/datos de tensores, compatibilidad de inferencia ni procedencia
  de shards sólo por pistas/nombres. Tampoco se implementan algoritmos Git/Xet ni un
  comprobador universal de todas las versiones/endian GGUF.
- La integración del hash en el motor real de transferencia/publicación de modelos se
  verifica en 009. Aquí se prueba el servicio incremental y la escritura reanudada con fixtures.
- Evidencia runtime con fixtures Native AOT, no carga de un modelo real por llama-server.
  El usuario aceptó la entrega y autorizó su cierre y commit.


### Aceptación
El usuario confirmó que todo es correcto, dio por terminada la tarea 008 y autorizó
el commit conjunto de implementación, pruebas y evidencia. No se amplía a 009 ni M2.
