---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '004'
- '007'
- '008'
created_at: 2026-10-07T22:02:18.126842Z
updated_at: 2026-10-08T18:04:21.516357500Z
completed_at: 2026-10-08T18:04:21.516357500Z
transitions:
- status: in-progress
  at: 2026-10-08T15:19:49.493772200Z
- status: complete
  at: 2026-10-08T18:04:21.516357500Z
---
# Descarga y publicación de modelos gestionados

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Integrar model download y listado local con selección fijada, transferencia y verificación. Publicar sólo unidad completa en models/usuario/repositorio/ruta-remota y registrar ID/nombre canónico case insensitive. Sin carpeta propia por cuantización ni companion implícito.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [x] Pruebas de múltiples shards, colisiones, rutas inseguras, espacio insuficiente y recuperación entre movimiento e índice. SHA-256 completo por archivo; contraste remoto sólo comparable. Ctrl+C/resume conserva commit y partes verificadas; éxito limpia operación y registra transacción. Sin externos/update/upgrade.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación aceptada y cerrada el 8 de octubre de 2026. SDK .NET 10.0.401, win-x64; sin paquetes nuevos ni legacy. Dependencias 003, 004, 007 y 008 completas.

### Alcance implementado
- `src/Coquena/ModelCli.cs`: `model download usuario/repositorio[:cuantización]` y
  `model list`. Integra el menú 007: README vuelve al mismo menú; cancelar no crea GUID;
  elegir inicia la descarga sin otra confirmación. Companions se rechazan antes de red.
- `src/Coquena/Models/ModelInstaller.cs`: adquisición inicial de una unidad completa,
  ID de registro distinto del GUID de operación, nombre case insensitive conservando
  escritura, configuración inicial vacía y archivos ordenados desde el primer shard.
  Rutas finales `models/<usuario>/<repositorio>/<ruta-remota>`, sin carpeta propia por
  cuantización ni companion implícito. Un vecino no se sustituye ni deduplica por hash.
- `src/Coquena/Models/HuggingFaceTransport.cs`: descarga HTTP convencional desde el
  commit fijado, también para Xet, sin clonar ni implementar el protocolo Xet. Resolución
  de redirecciones HTTPS explícita, máximo cinco saltos, hosts HF/CDN bajo los sufijos
  `huggingface.co` y `hf.co`, sin puertos alternativos, userinfo, cookies ni credenciales.
  Conserva Range/IfRange, contrasta X-Repo-Commit cuando se informa; no persiste URLs firmadas.
  RateLimit puede aumentar Retry-After; esperas excesivas se devuelven como fallo recuperable.
- `src/Coquena/Downloads/DownloadManager.cs`: sesión SHA-256 por archivo de modelo al
  escribir, con buffer de 64 KiB. Al reanudar lee el prefijo completo antes de añadir bytes;
  no serializa estado criptográfico. Los archivos transferidos se contrastan íntegramente
  contra su referencia antes de reutilizarlos y no se vuelven a descargar. Backends siguen
  sin calcular ni almacenar hashes. Una discrepancia remota no redefine ninguna referencia.
- Persistencia source-generated AOT: campos opcionales LocalIntegrity/VerifiedWriteUtc
  por transferencia, LastWriteUtc por archivo instalado y ModelInstallPlan en operación.
  Compatibilidad con documentos previos sin estos campos; no se reemplaza el contrato
  genérico de transferencias por una operación específica de HF. Validación del plan,
  referencias remotas fijadas, progreso de movimientos y unidad verificada.
- Listado exclusivamente local: encabezados GGUF acotados y presencia/lectura de shards;
  aviso por tamaño/fecha modificados sin actualizar catálogo ni referencias. No exige
  hash completo, red ni certifica compatibilidad de inferencia.
- `DownloadCli` integra resume/pause/cancel/status para operaciones de modelos concretas,
  conservando el GUID, selección y controles del motor 004. Ctrl+C del ejecutor usa la
  cancelación conservadora del motor (pausa, no cancelación destructiva).

### Publicación y decisiones técnicas
- Reserva de nombre/destinos al persistir la operación bajo la exclusión de escritores
  existente. No se mantiene el bloqueo global durante red ni durante hashes completos.
  Operaciones distintas pueden transferir simultáneamente; la publicación vuelve a leer
  catálogo y destinos bajo exclusión y combina registros sin perder otro escritor.
- Preflight de espacio conocido: suma de bytes restantes más 1 MiB para metadatos.
  Consulta el volumen del home; no duplica pesos entre preparación y destino. Es una
  estimación, no una reserva del disco; totales desconocidos/concurrencia/errores reales
  de escritura siguen sujetos a fallo recuperable, sin anunciar instalación.
- Preparación permanece en `downloads/<guid>/files/<índice>.part`. El inspector 008
  recibe nombres lógicos finales y un resolver local de rutas; verifica nombres/orden
  y metadatos de división sin renombrar parciales ni crear sidecars por shard.
- Verificado el conjunto, persiste referencias completas y fase publishing antes de
  mover. Cada archivo tiene intención MovingFile persistida antes del movimiento y
  checkpoint MovedFiles después. Todos los movimientos e índice se coordinan en una
  región corta no cancelable, pero se reconoce que no son una transacción del filesystem.
- Fallo entre movimiento y checkpoint/índice: resume localiza sólo las rutas exactas
  del plan, exige ausencia de copia competidora, vuelve a calcular hashes y comprueba
  GGUF completo. Existir en disco no activa la unidad. No consulta otra revisión ni red.
  Un registro ya publicado debe coincidir con ID, selección, referencias y rutas del plan.
- Handles de lectura sin compartir escritura, pero compartiendo borrado, permanecen
  abiertos durante validación/movimiento/publicación en Windows. No se promete inmunidad
  a toda modificación externa, reemplazo de ruta o atacante con acceso al home.
- Cancelar una publicación interrumpida no indexada comprueba hashes y devuelve sólo
  los archivos propios a preparación antes de limpiarla. La fase cancelling-publication
  hace recuperable una reversión interrumpida: repetir cancel, no continuar una descarga.
  Archivo de destino cambiado/ambiguo no se elimina por adivinación. Pueden quedar carpetas
  finales vacías; no se borra recursivamente el repositorio ni archivos vecinos.
- Tras activar el índice, cancel conserva el modelo y completa limpieza; nunca revierte
  la instalación publicada. Limpieza fallida deja estado completed/cleanup con diagnóstico
  y GUID recuperable. Éxito elimina toda carpeta de operación y registra Installed en el
  JSONL limitado existente. Los eventos de reintentos no prometen semántica exactly-once.

### Validación reproducible
```powershell
leanspec board
leanspec search modelos
leanspec view 009
dotnet build Coquena.slnx -c Release
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
git diff --check
```

### Resultados
- 118 comprobaciones nuevas administradas; 120 en runner Native AOT con el ejecutable
  publicado: monofichero/shards, hashes completos, dominios no comparables/ausentes,
  discrepancia remota sin rebase, GGUF ilegible/metadatos contradictorios, rutas inseguras,
  colisiones case insensitive/en disco/tardías, tamaños, espacio y reserva concurrente.
- Pausa por token del mismo camino que Ctrl+C: primer shard completo conservado, segundo
  a offset 97; resume solicita sólo el segundo desde ese offset y verifica su SHA completo.
  Manipular el primer parcial completo impide su reutilización antes de red.
- Fallos inyectados después del primer movimiento y después del índice: recuperación
  sin transporte y sin confundir presencia con activación. El proceso `coquena.exe
  download resume <guid>` recupera el movimiento pendiente en un home temporal sin red;
  `coquena.exe model list` comprueba la consulta local real. Menu/download con proveedor
  y transporte inyectados también ejecutado dentro del runner Native AOT.
- Cancelación tras movimiento incompleto, corrupción del destino recuperado, repetición
  de cancel después de diagnóstico y limpieza fallida tras activación; vecinos conservados.
  Publicaciones simultáneas diferentes retienen ambos registros. Fixtures de red cubren
  CDN, Range/IfRange, ausencia de credenciales, redirect malicioso/ciclo, commit y RateLimit.
- Regresiones completas correctas: nueve casos CLI administrados y nueve del binario,
  contratos/persistencia, backends 180/181, transferencias 54/54, instalación de backends
  56/56, HF/selección 168 y GGUF/integridad 127. Publicaciones CLI y runner AOT con
  warnings-as-errors y ejecución final exitosa (no sólo compilación).
- La comprobación nativa inicial del mensaje acentuado encontró una suposición de
  UTF-8 en el harness respecto de la consola Windows. La aserción usa ahora resultado,
  contenido ASCII y catálogo para verificar semántica, sin cambiar la codificación pública.
- Fixtures pequeñas en homes temporales exclusivos. No se descargaron pesos reales ni
  se escribió en ${CoquenaHome}. Consulta web limitada a documentación oficial HF para el
  contrato de revisión/redirecciones. No se modificaron PlantUML ni documentos canónicos;
  enlaces locales de esta tarjeta y diff revisados. No se usó validate/deps/tokens como
  evidencia mientras siga la limitación conocida del adaptador LeanSpec.

### Referencias verificadas y límites
- [Descarga con revisión fijada](https://huggingface.co/docs/huggingface_hub/package_reference/file_download).
- [Hosts de almacenamiento y CDN](https://huggingface.co/docs/hub/models-downloading#downloading-behind-a-proxy-or-firewall).
- [Ruta LFS compatible para Xet](https://huggingface.co/docs/hub/xet/file-id).
- Sin resolver la selección semántica de cuantización para nombres no convencionales (por ejemplo, `Q4_0` con guion bajo antes de la cuantización); el caso se mantiene en la selección Hugging Face/tarea 007 y no se atribuye a la descarga. Sin registro externo, transición a gestionado, companions, verify público, update,
  upgrade, remove ni configuración/presets/servidores. Corresponden a otras tarjetas/M2.
- Inspección de encabezados no valida pesos/descriptores de tensores ni garantiza carga
  por llama-server. El recorrido real con dos modelos e inferencia pertenece a 014.
- Transporte probado con respuestas simuladas, no con descarga real de un modelo del Hub;
  no se certifica que todo CDN futuro preserve siempre validadores o permita Range.
  Cambio de identidad/Range no reutilizable se diagnostica y no cambia el commit; reinicio
  de archivo requiere el consentimiento explícito previsto por download resume.
- Siguiente tarjeta del recorrido: 010, configuración/presets e INI.
