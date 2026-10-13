---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
- 031-m2-model-upgrade
created_at: 2026-10-10T16:36:45.019442500Z
updated_at: 2026-10-13T14:18:03.726119100Z
completed_at: 2026-10-13T14:18:03.726119100Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.968458800Z
- status: in-progress
  at: 2026-10-13T12:40:25.317770400Z
- status: complete
  at: 2026-10-13T14:18:03.726119100Z
---
# Convertir un registro externo en instalación gestionada

## Objetivo
Cambiar explícitamente la propiedad de un registro externo tras verificar una nueva descarga
gestionada.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#pasar-un-externo-a-gestión-de-coquena)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Ofrecer transición explícita al descargar nombre ocupado por externo; cancelar/rechazar
  conserva el registro, otras colisiones se rechazan.
- [x] Completar/verificar descarga antes de cambiar propiedad/rutas conservando
  ID/referencias; nunca tocar archivos externos originales.
- [x] Comprobar vigencia antes de publicar y probar principal/companion/shards, fallos e
  interrupciones sin retirar prematuramente el externo.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No mueve, borra ni adopta los archivos externos originales.

## Implementación y decisiones — 2026-10-13

Implementación acotada a esta tarjeta, aprobada y cerrada por el usuario el
2026-10-13. Implementación, pruebas, documentación, evidencia y mejora del reporte
de entrega en AGENTS.md se entregan en un único commit. Dependencias 026 y 031
completas, comprobadas mediante board/search/view y sus tarjetas.

- Tras seleccionar una descarga cuyo nombre coincide con un externo, el menú ofrece
  pasar a gestión o conservar el externo y cancelar. Rechazo/EOF no crea operación;
  una elección inválida repite la propuesta. Otras colisiones conservan su rechazo.
- `externalPrevious` opcional en el plan persistido conserva el registro de partida,
  separado del respaldo de un upgrade gestionado. Validación exige propiedad externa,
  mismo ID/nombre/tipo y ausencia de plan de upgrade o respaldo de archivos originales.
- El GUID fija selección, commit y directorio configurado. Reserva la unidad bajo la
  exclusión compartida con start; rechaza uso activo/identidad incierta y bloquea inicio
  y mutaciones de metadatos durante la operación. Conserva parámetros y referencias
  por ID; descarta observaciones de integridad/equivalencia de los archivos externos
  al publicar la nueva instalación, sin tratarlas como evidencia de los nuevos bytes.
- Verifica todos los archivos y el conjunto GGUF antes de publicar; comprueba el
  registro de partida bajo exclusión antes de mover/indexar. Destinos que coinciden
  con rutas externas se rechazan aunque exista consentimiento. No lee originales
  para preparar ni respaldar: admite la transición de un externo incompleto/ausente.
- Pause/resume/cancel reutilizan la recuperación por GUID. Hasta cambiar el índice
  conserva el externo; cancelar una publicación interrumpida retira sólo destinos
  preparados propios y verificados, rechazando rutas ahora referenciadas por otro
  registro. Después de indexar conserva la instalación gestionada y completa limpieza.
- Espacio previo incluye preparación y copia al destino, sin respaldar externos.
  No se añadieron paquetes, descargaron pesos reales ni inició llama.cpp.

## Evidencia y reproducción

- Suite de instalación/transición administrada: 227 comprobaciones, exit 0;
  `artifacts/task032-checks/model-install-managed.txt`, stderr vacío.
- Regresión administrada completa final: exit 0;
  `artifacts/task032-checks/managed-tests.txt`, stderr vacío.
- CLI y runner publicados Native AOT win-x64 con advertencias AOT/trimming como
  errores: exit 0; `publish-product.txt` y `publish-runner.txt` en la misma carpeta.
- Suite de instalación/transición nativa: 236 comprobaciones, exit 0;
  `model-install-tests.txt`, stderr vacío. Incluye reanudación de transiciones
  persistidas mediante el CLI nativo, principal/mmproj/mtp y shards completos.
- Regresión completa final con runner y CLI Native AOT: exit 0;
  `native-tests.txt`, stderr vacío en `native-errors.txt`. Incluye contratos,
  almacenamiento, configuración, servidores/procesos, backends, transferencias,
  HF/GGUF, modelos/companions, directorios, externos, update y upgrade.
- Menús de rechazo/EOF/aceptación, pausa, hash incorrecto, interrupción después de
  mover e indexar, cancelación antes/después de publicar, snapshot obsoleto, rutas
  externas coincidentes, directorio configurado, ID/parámetros/referencias y uso
  activo/incierto cubiertos con archivos pequeños, transporte simulado y homes temporales.
- Documentación: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales válidos;
  `docs.txt`. `git diff --check` sin errores.

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --model-install-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task032-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task032-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task032-checks/run-native.ps1 -Filter '--model-install-tests' -OutputName 'model-install'
& artifacts/task032-checks/run-native.ps1 -OutputName 'native'
& artifacts/task032-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en artifacts/task032-checks/. El launcher nativo normaliza
Path/PATH al iniciar procesos, conservando el entorno.
