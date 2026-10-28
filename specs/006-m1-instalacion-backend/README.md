---
status: archived
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '004'
- '005'
created_at: 2026-10-07T22:02:15.195656900Z
updated_at: 2026-10-28T10:21:41.628934800Z
completed_at: 2026-10-08T02:14:07.276661Z
transitions:
- status: in-progress
  at: 2026-10-08T01:54:08.568309700Z
- status: complete
  at: 2026-10-08T02:14:07.276661Z
- status: archived
  at: 2026-10-28T10:21:41.628934800Z
---
# Instalación y selección del backend oficial

## Objetivo
Integrar backend remote/install/list/use con transferencia común; preparar, extraer con rutas seguras y comprobar llama-server --version antes de publicar. Primer backend propio instalado autoasigna default si procede. Concretar layout recuperable y conservar intención latest/pin.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Pruebas de archivo malicioso/incompleto, --version inválido, colisión y fallo entre movimiento e índice. Reanudar instalación sin cambiar release; no registrar parciales. No calcular ni persistir SHA-256 para backends. Sin externos ni update/upgrade.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación y verificaciones realizadas el 7 de octubre de 2026 (hora local),
.NET SDK 10.0.401, Windows x64. Sin commits ni paquetes nuevos; no se consultó legacy.

### Decisión funcional confirmada
El usuario confirmó que repetir backend install es una forma forzada de descargar y
reinstalar. Latest resuelve nuevamente el filtro; el build ya registrado conserva ID.
Se actualizó la regla canónica en servidores.md, su enlace en comandos.md y los
flujos de actividades/secuencias. Se retiró del diagrama la antigua salida inmediata
cuando ya estaba instalado. No se implementaron update/upgrade ni su retención.

### Implementación y decisiones técnicas
- Integrados backend remote/install/list/use y download resume para backends oficiales.
  List/use son locales, sin sondeo de hardware, metadatos ni red. Use exige una
  instalación registrada disponible; normaliza oficial/ y admite familia:release.
  No anuncia externos, uninstall, update/upgrade ni funcionalidades de otros milestones.
- Selección de 005 y motor de 004 reutilizados. Install crea GUID y selección completa
  antes de bytes; conserva RequestedRelease (latest/pin) además del release fijado,
  artefactos/tamaños y snapshot del registro/seguimiento anterior. Resume no resuelve latest.
- Ubicación de artefactos efímera: consultar el tag fijado y comprobar nombres/tamaños;
  no persistir URLs ni digest. Redirecciones explícitas hasta cinco saltos HTTPS,
  sólo github.com y subdominios githubusercontent.com, sin cookies/credenciales ni
  descompresión; preservar Range/If-Range. Metadatos con timeout de 20 s; transferencia
  conserva los límites/reintentos/checkpoints de 004.
- Extraer bajo downloads/<guid>/extracted. ZIPs con límites conjuntos de 4096 entradas
  y 8 GiB descomprimidos, streaming de 64 KiB; rechazar escapes, rutas absolutas,
  nombres Windows inválidos, duplicados case-insensitive, symlinks/reparse points y
  colisiones entre artefactos. Se admite repetir entradas de directorios comunes.
  No calcular, guardar ni verificar SHA-256 en ningún paso de backends.
- Exigir exactamente un llama-server.exe, proceso --version con exit code 0,
  build coincidente con el release fijado y formato reconocido. Timeout 15 s,
  salida acotada a 32 Ki caracteres por stream; al cancelar/fallar, terminar y esperar
  el proceso hijo. No se deduce autenticidad, compatibilidad de inferencia ni familia
  a partir del sondeo. Probe y fallos de publicación inyectables en pruebas.
- Descubrimiento por ejecución real: b11476 informa
  version: 0.6.0-dev (build 11476, commit 988190680), no sólo el formato histórico
  version: <build> (<commit>). Se soportan ambos con comprobación del build y una
  única línea de versión. Se agregó fixture del formato actual.
- Slots independientes backends/<backend-id>/<operation-guid>/; reinstalar no escribe
  sobre ejecutables activos ni sobre una preparación ajena. Un build existente conserva
  ID; uno nuevo obtiene GUID. BackendsDocument guarda TracksLatest y Pinned por registro;
  sólo hay un actual por origen/variante/plataforma, independiente de pins instalados.
  Campos opcionales nuevos mantienen lectura de fixtures/esquema 1 de las predecesoras.
- OperationDocument guarda BackendInstallPlan opcional; contiene snapshot previo,
  slot, intención y ruta/versión validadas. Guardar publishing antes de mover; un marker
  .coquena-owner viaja con la preparación. Recuperar un movimiento exige propiedad,
  plan, sondeo nuevamente válido y comparación del catálogo, no mera existencia.
- Lease de ejecutor de 004 ahora abarca validación, publicación y limpieza. Publicación
  corta bajo el writer lease del home: revalidar snapshots/colisiones/uso, mover al slot,
  actualizar índice, autoasignar default si es la primera instalación y no existe uno,
  guardar completed/cleanup. No sostener writer lease durante red, extracción ni probe.
  Latest posterior cambia sólo su seguimiento; no convierte un default fijado ni reemplaza
  otra selección explícita. Use fixed protege el pin para la retención futura.
- Una preparación obsoleta se rechaza, no gana por publicar más tarde. Recuperación tras
  movimiento o índice conserva release y no vuelve a descargar; no revierte un registro
  ya publicado. Cancel antes del commit retira sólo su slot propio; después del commit
  completa limpieza sin borrar el backend instalado ni anunciar una reversión falsa.
- Limpieza exitosa elimina slot sustituido, parciales y operation.json y registra Installed
  en el JSONL limitado de 003. Cleanup fallido conserva completed/cleanup recuperable,
  separado de un backend inválido. No hay historial ilimitado de operaciones.
- Hasta que 012 integre identidad verificable, cualquier InstanceDocument que referencie
  el ID sustituido bloquea reinstalación (incluida identidad incierta); no se mata ni
  presume muerto un proceso por PID. Esta restricción conservadora evita anticipar 012.

### Verificaciones reproducibles
```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
$env:COQUENA_HOME = Join-Path $PWD 'artifacts/integration/006/home'
& artifacts/publish/win-x64/coquena.exe backend install cpu --release b11476
& artifacts/publish/win-x64/coquena.exe backend list
& artifacts/publish/win-x64/coquena.exe backend use
& artifacts/publish/win-x64/coquena.exe backend install cpu --release b11476
& artifacts/publish/win-x64/coquena.exe backend install cpu
& artifacts/publish/win-x64/coquena.exe backend use cpu:latest
& artifacts/publish/win-x64/coquena.exe download list
plantuml -checkonly 'artifacts/validation/006/*.puml'
git diff --check
```

- Runner administrado: 52 comprobaciones nuevas de instalación/reinstalación OK;
  regresiones originales OK (nueve casos CLI, contratos, persistencia, 180 comprobaciones
  de selección de backends y 53 comprobaciones download).
- Runner Native AOT: 56 comprobaciones de instalación, incluidas extracción y ejecución
  real de un fixture nativo, rechazo de un archivo no ejecutable y proceso con exit code
  no cero; regresiones OK, 181 comprobaciones backend, 54 download y nueve invocaciones
  de la CLI publicada. CLI y runner publicados sin warnings/errores, AOT warnings-as-errors.
- Fixtures prueban escapes, archivo incompleto, exe ausente/duplicado, versión inválida
  o de otro build, symlink, límite de entradas, varios artefactos, colisión de destino,
  preservación del anterior, reinstalación/ID/pin/default, avance de latest, plan obsoleto,
  fallos después de movimiento e índice, cancel de un slot movido no publicado, cleanup
  bloqueado por un archivo abierto, uso incierto y pausa durante validación.
  Resume de publicación/cleanup/validación prueba cero llamadas al transporte.
- Proveedor real con CLI Native AOT en home aislado: CPU b11476 (ZIP 19441535 bytes),
  instalación y reinstalación con ID 05404278-2269-4b3d-9444-82005491b6db conservado,
  default fijado, slot previo retirado y sin operaciones pendientes al terminar.
  Latest resolvió b11481 (19459260 bytes); mantiene b11476 protegido como pin, registra
  b11481 como actual y backend use cpu:latest persiste/muestra su seguimiento.
- El primer intento real b11476 diagnosticó invalid-server-version; tras corregir el
  formato, resume 4a61bfc4-ffda-494c-942d-61569c3ab6ee completó con los ZIPs ya transferidos.
  El intento b11481 tuvo un fallo local de E/S durante publishing; no se adjudica causa
  sin evidencia. Resume 3b9de160-b62b-48ec-b23d-e7556a16a76d completó correctamente sin
  otra descarga ni cambio de release. No se agregó reintento implícito de publicación.
- 26 bloques PlantUML extraídos de los cinco documentos revisados y -checkonly OK;
  enlaces locales y git diff --check OK. JSON/JSONL comprobados por contratos y pruebas.
  Tablero, alcance, prerrequisitos y evidencia revisados mediante board/search/view.

### Referencias verificadas del proveedor
- [API oficial de releases GitHub](https://docs.github.com/en/rest/releases/releases): tag fijado y browser_download_url.
- [Release oficial b11476](https://github.com/ggml-org/llama.cpp/releases/tag/b11476) y recorrido real del ZIP CPU.

### Límites y siguiente paso
- Recorrido real sólo CPU win-x64, sin pesos ni inferencia; CUDA/dependencias con fixtures,
  no instalación real de CUDA/Vulkan. Sondeo --version no certifica toda inferencia.
- Exclusión para procesos Coquena cooperantes en Windows local; no garantía contra
  cambios hostiles de rutas/reparse points, archivos externos o filesystem de red.
- No hay atomicidad multidocumento entre disco, índice, config y JSONL. El plan persistido
  permite recuperación; un cierre durante auditoría/limpieza puede requerir resume
  explícito y puede producir eventos Installed repetidos, sin reproducción de eventos.
- Sin señal física Ctrl+C nueva: cancelación del token durante validación probada, más
  controles y procesos nativos de 004. Sin carga real grande que alcance el límite de
  8 GiB ni prueba real de desconexión del CDN; límites de entradas/transporte con fixtures.
- M1 no está terminado. Siguiente tarjeta por orden: 007, selección Hugging Face;
  008 también tiene su propio alcance. Sin paquetes nuevos ni commits.

### Aceptación
El usuario confirmó que todo es correcto, dio por terminada la tarea y autorizó el commit
de implementación, pruebas, documentación y evidencia de 006.
