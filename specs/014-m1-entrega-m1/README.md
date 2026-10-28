---
status: archived
created: 2026-10-07
tags:
- m1
depends_on:
- '013'
created_at: 2026-10-07T22:02:23.095115200Z
updated_at: 2026-10-28T10:21:41.639282700Z
completed_at: 2026-10-09T18:05:28.730612300Z
transitions:
- status: in-progress
  at: 2026-10-09T17:25:30.663421400Z
- status: complete
  at: 2026-10-09T18:05:28.730612300Z
- status: archived
  at: 2026-10-28T10:21:41.639282700Z
---
# Recorrido real y entregable Native AOT M1

Las referencias Git de Coquena de este documento apuntan a sus equivalentes en el historial saneado. Los hashes de modelos y backends externos se conservan.

El hardware del entorno de prueba se ha anonimizado. Los resultados conservados describen esa ejecución y no garantizan el mismo rendimiento en otro equipo.

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Preparar paquete ejecutable y guía breve reproducible. Acordar backend/build y dos modelos distintos adecuados a RAM/VRAM antes de descargar gigabytes; fijar recursos usados y evidencia. Ejecutar aceptación completa de M1, no implementar funcionalidades M2.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [milestones/M1-servidor-local.md](../../docs/milestones/M1-servidor-local.md)

## Trabajo y aceptación
- [x] Aceptar previamente [015 — resolución robusta de unidades GGUF de Hugging Face](../015-m1-resolucion-gguf-hugging-face/README.md), incluido el caso Google/Gemma con y sin cuantización explícita. Es un bloqueo de aceptación de M1, no un motivo para detener las tareas independientes 010–013. La relación se expresa aquí porque `leanspec rel` informa que todavía no está migrado al adaptador; no se editaron metadatos manualmente.
- [x] Probar instalación, descarga/pausa/resume, dos presets con inferencia, métricas/logs, -d/stop/restart y fallos de dependencias/puerto/shards. Pruebas automáticas pequeñas más integración real opt-in con binario publicado; registrar resultados, límites y pasos para el usuario.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
### Inicio del recorrido — 9 de octubre de 2026

Dependencias 013 y 015 completas, comprobadas con `leanspec board`, `search 014`
y `view`; la aceptación de 015 está registrada en su tarjeta. Estado 014 cambiado
a in-progress antes de preparar el entregable. Checkout inicialmente limpio.

Inspección local: capacidad de memoria anonimizada y GPU de prueba con capacidad de memoria anonimizada,
driver de prueba anonimizado. ${CoquenaHome} contiene backend oficial cuda@13 b11482, commit
b86d2f075, y google/gemma-4-26B-A4B-it-qat-q4_0-gguf:Q4_0, commit
d1c082be9cf3c8a514acf63b8761f4b41935842e (14 439 363 584 bytes).
Son registros existentes, no evidencia nueva de inferencia ni de integridad actual.
Se consultó al usuario backend/build, segundo modelo y límite de nuevas descargas;
el recorrido con recursos reales depende de ese acuerdo explícito.

Suite administrada completa: exit 0. CLI y runner publicados Native AOT win-x64
con IlcTreatWarningsAsErrors, TrimmerSingleWarn=false y
SuppressTrimAnalysisWarnings=false, sin advertencias. La CLI publicada ejecuta
`--help` correctamente. Suite nativa completa: exit 0, incluidos nueve casos CLI
en ambos modos, 75 de configuración, 68 de definiciones, 20 de observabilidad,
64 de procesos, 181 de backends, 54 de transferencias, 56 de instalación de
backend, 217 de resolución, 168 de Hugging Face, 127 de GGUF/integridad y
120 de instalación de modelos, además de contratos y persistencia.
`git diff --check` correcto.

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task014-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task014-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task014-tests-native/Coquena.Tests.exe artifacts/task014-native/coquena.exe
& artifacts/task014-native/coquena.exe --help
git diff --check
```

En ese punto inicial estaban pendientes el acuerdo de recursos, la guía,
instalación/pausa/resume e inferencia reales, métricas/logs y ciclo de procesos.
Los resultados finales se registran abajo; la suite por sí sola no sustituyó
el recorrido real. Sin paquetes nuevos ni commits.

### Segundo modelo seleccionado por el usuario

El usuario eligió `prism-ml/Ternary-Bonsai-27B-gguf:Q2_0`.
Inspección con runner Native AOT, sin descargar pesos completos:

```powershell
& artifacts/task014-tests-native/Coquena.Tests.exe --hf-resolve prism-ml/Ternary-Bonsai-27B-gguf:Q2_0
```

Commit resuelto: `86e89f34c93201c3dfd5e5880fedb0022fc7e34d`. El selector
ofrece dos unidades independientes: `Ternary-Bonsai-27B-Q2_0.gguf`
(7,17 GB / 6,67 GiB) y `Ternary-Bonsai-27B-Q2_g64.gguf`
(7,59 GB / 7,06 GiB). El harness selecciona la primera sólo para probar
resolución; eso no constituye elección del usuario ni autorización para
resolver la ambigüedad por orden. El menú del producto debe elegir una unidad.

La [documentación del publicador](https://huggingface.co/prism-ml/Ternary-Bonsai-27B-gguf#llamacpp-cuda)
indica Q2_g64 para llama.cpp upstream y PQ2_0 para su fork con kernels g128.
Se preguntó cuál de las dos unidades Q2_0 usar, recomendando Q2_g64 para
el backend oficial. No se adopta ni compila un fork como efecto secundario.
En ese punto la compatibilidad efectiva con b11482 estaba pendiente de
inferencia; se comprobó en el recorrido final de Q2_g64 registrado abajo.

Inspección: exit 0, 34 799 616 bytes de rangos, 72 peticiones, tres cabeceras,
buffer máximo 2 162 689 bytes, 20,89 s. Sin GUID, parciales ni instalación.

### Recorrido real completado

El usuario eligió explícitamente Q2_g64 y autorizó inicio e inferencia local en
127.0.0.1:18080. La primera invocación combinada de inicio/consulta fue rechazada
por revisión automática con `blocked by policy`, sin más detalle; no se ejecutó.
Tras la confirmación explícita, las acciones de inicio y consulta funcionaron.

CLI utilizada: `artifacts/task014-native/coquena.exe`, Native AOT del HEAD
`75b523292b7d57d194a0fe390add73895415bb9b`; cambios de 014 sólo de documentación.
SHA-256 del ejecutable: `c4b4559d010f1fcce2b06703f60b2ecd8d3ddc4f0e64087659f936ff7dcfc437`.

- Backend instalado desde los dos ZIP oficiales en home aislado
  `artifacts/task014-install-home`: 576 820 836 bytes en total. Operación
  `3b1ff98d-782b-462f-aba7-5c54bb110e27`, pausa/reanudación, publicación y
  limpieza correctas, exit 0. Catálogo muestra cuda@13:b11482 fijado,
  ID `428d33e4-b131-49af-8825-f7f7188887d3`; probe local de versión correcto.
- Bonsai descargado mediante menú real, opción Q2_g64, en ${CoquenaHome}.
  Operación `5b05694a-f682-4c93-b4a0-03611fea5677`, pausa mediante comando
  a 127 407 295 bytes; el ejecutor finalizó y resume continuó la selección
  fijada, sin otra exploración. Archivo final: 7 585 330 240 bytes,
  SHA-256 `59a45d1ecef702b14531b06d22949f33b25c1897da31a8c0b298e01e4d9138eb`.
  Validación/publicación y limpieza correctas, exit 0. Gemma se reutilizó de
  la instalación existente: no se volvió a descargar sus 14,44 GB.
- Servidor separado `m1-acceptance`, puerto 18080, backend fijado
  ggml/cuda@13:b11482, metrics activas y models-max=1. Presets explícitos
  `m1-gemma` y `m1-bonsai`, ambos ctx-size=2048. Gemma usa n-gpu-layers=20
  para no exigir todos sus pesos en 12 GiB; Bonsai usa all.
- Ambos presets respondieron `Hello!` a `Reply with a short greeting.`.
  Gemma primero se probó solo y después en el mismo router con Bonsai.
  Alternancia Bonsai → Gemma en instancia `42a1baea-a39e-4309-a91b-18b353aa74cd`
  correcta: el anterior queda unloaded y el actual ofrece métricas.
  Bonsai: 16 tokens prompt, 228 generación, 71,6501 tokens/s.
  Gemma en ese router: 22 tokens prompt, 76 generación, 34,4784 tokens/s.
  Son una petición por preset, no benchmarks de rendimiento o calidad.
- La primera petición Gemma limitada a 64 tokens sólo produjo reasoning_content
  y finish_reason=length. No se contó como respuesta final: se repitió con
  max_tokens=256, obtuvo contenido no vacío y finish_reason=stop. La guía
  refleja el presupuesto corregido, sin modificar producción.
- -d devuelve el prompt y el servidor conserva identidad al cerrar el proceso
  CLI iniciador; otra CLI consulta ps e inferencia. Logs recientes disponibles.
  Ctrl+C real sobre logs -f termina el seguidor, ps conserva la misma instancia
  `1a561090-9d6a-496d-b2da-c855daf2b8de` y sus métricas. El wrapper PTY informa
  exit 1 al enviar Ctrl+C; no se afirma exit 0 de ese wrapper.
- Start en primer plano mostró logs; Ctrl+C solicitó parada ordenada de instancia
  `1b601cf6-f28d-44a9-ba31-e4ab536eca54`; ps posterior no mostró activos.
  Restart de router activo reemplazó la instancia 42a1baea por
  `b2c8119a-d95b-4328-b751-6f93a423fbd2`, conservó ambos presets y devolvió
  prompt. Stop funcionó; logs siguen consultables tras parar. Estado final:
  servidor de aceptación detenido, presets/modelos/configuración conservados.
- Puerto ocupado real: TcpListener loopback en 18080, start rechazado con exit 1
  y diagnóstico sin reasignación, ps sin activos. Backend ausente, referencias
  rotas y shards faltantes se verificaron con las fixtures de la suite nativa;
  no se mutilaron los modelos del usuario para simular esos fallos.

Comandos de configuración, inferencia y consulta en [recorrido.md](recorrido.md).
Evidencia adicional local bajo artifacts: task014-download.log,
task014-backend-install.log, task014-gemma-router-response.json,
task014-bonsai-response.json, task014-*-metrics.txt, task014-router-models.json,
task014-port-check.txt y task014-after-stop-logs.txt. No contienen credenciales
ni URLs de transferencia. Sin cambios de runtime, dependencias ni comandos nuevos.

La prueba real fue opt-in por elección y autorización explícitas del usuario.
No se acredita contexto grande, multimodalidad, companions, otras GPUs ni
respuestas útiles en tareas extensas. Gemma reutiliza su integridad registrada;
014 no ejecutó un nuevo hash completo de sus bytes. Instalación de Bonsai sí
verificó el hash completo. La retención continua configurable por CLI pertenece
a 017; cambio de default de puerto pertenece a 016, fuera de esta entrega.

El usuario aprobó la entrega el 9 de octubre de 2026, declaró terminada la tarea
y autorizó un único commit con la guía, evidencia y cierre de 014. Se actualiza
el estado a complete mediante LeanSpec. La aceptación incluye los límites
documentados del recorrido real y del paquete.

### Paquete y comprobaciones finales

Paquete local `artifacts/coquena-m1-win-x64.zip` (4 484 932 bytes):
coquena.exe, README.md breve y recorrido.md. No incluye modelos, backend ni PDB.
SHA-256 ZIP: `3d37462f6075820737f0773dcd81f98aea298b954c0652759f42f33f7fe5efd4`.
Extraído en `artifacts/task014-unpacked`: hash del ejecutable coincidente y
`coquena.exe --help` exit 0. El backend recién instalado también ejecutó
`--version`: build 11482, commit b86d2f075, Clang 20.1.8 Windows x86_64.

Documentación: 37 diagramas PlantUML, tres ejemplos JSON y 110 enlaces de la
documentación canónica/013/017 correctos mediante el script existente
`artifacts/task013-checks/validate-docs.ps1`; enlaces de ambos documentos 014
comprobados además explícitamente. Sin nuevos diagramas ni ejemplos JSON.
`git diff --check` correcto. Cambios del checkout: sólo esta tarjeta y recorrido.md.

Para reconstruir el paquete después de publicar con los comandos registrados:

```powershell
New-Item -ItemType Directory -Path artifacts/task014-package -Force
Copy-Item artifacts/task014-native/coquena.exe artifacts/task014-package/coquena.exe
Copy-Item specs/014-m1-entrega-m1/recorrido.md artifacts/task014-package/recorrido.md
# README.md breve del paquete: fecha, hash del exe, recursos y límites de esta evidencia.
Compress-Archive -LiteralPath artifacts/task014-package/coquena.exe,artifacts/task014-package/README.md,artifacts/task014-package/recorrido.md -DestinationPath artifacts/coquena-m1-win-x64.zip -Force
Expand-Archive artifacts/coquena-m1-win-x64.zip artifacts/task014-unpacked -Force
& artifacts/task014-unpacked/coquena.exe --help
```

El hash ZIP corresponde al paquete entregado: timestamps de un nuevo archivo
pueden cambiarlo. No se afirma reproducibilidad bit a bit del ZIP.
