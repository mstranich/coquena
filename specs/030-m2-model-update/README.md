---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:44.459342600Z
updated_at: 2026-10-13T01:21:35.579839300Z
completed_at: 2026-10-13T01:21:35.579839300Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.803296900Z
- status: in-progress
  at: 2026-10-13T00:49:17.500335300Z
- status: complete
  at: 2026-10-13T01:21:35.579839300Z
---
# Detectar y resolver actualizaciones de modelos y companions

## Objetivo
Detectar cambios remotos y resolver planes de actualización explicables sin modificar la
instalación.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#seguimiento-y-actualización-de-gestionados)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar model update y companion update separando comparador puro del transporte HF
  y guardando planes de commit fijo.
- [x] Probar identidad tipada, cambio sólo de README, pesos cambiados, reubicación exacta
  única, rutas desaparecidas y evidencia no comparable.
- [x] Resolver ambigüedad/sucesores con menú explícito sin mover archivos/descargar
  pesos/cambiar cuantización automáticamente.
- [x] Distinguir consulta fallida/intervención/orfandad y conservar fechas de último
  intento/éxito; inventario incompleto no prueba ausencia.
- [x] Cubrir alcance global con companions, filtro companion, externos excluidos y errores por
  unidad.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No aplica planes, mueve archivos o certifica integridad.

## Implementación y evidencia

Implementación aprobada, tarea cerrada y entregada en un único commit con código, pruebas, documentación y evidencia. Dependencia 027 complete verificada mediante board/search/view.
Las reglas y el alcance se resolvieron con la documentación canónica; no quedan
ambigüedades que requieran una decisión del usuario.

- `model update [nombre-modelo]` consulta gestionados; sin nombre incluye principales,
  mmproj y mtp. `model companion update [nombre-companion]` limita el ámbito a auxiliares.
  Externos no consultan la red ni reciben observación de seguimiento.
- Comparador puro separado del transporte HF: compara algoritmo, dominio y valor.
  Un commit/README/tamaño no reemplaza identidad de contenido. Resuelve conjuntos
  completos, incluyendo reubicación exacta única y candidatos ambiguos.
- Observación opcional en models.json, compatible con registros previos. Conserva
  último intento/éxito, resultado/diagnóstico anterior y seguimiento elegido separado
  de instalación. Plan con selección instalada de partida, commit objetivo, conjunto
  completo, nombre objetivo y acciones reuse/download por archivo.
- Menú explícito con diferencias y cancelación. La selección asistida usa el resolver
  GGUF existente para nombres no convencionales, con inspección remota acotada.
  Conserva la cuantización resuelta por cabecera y sigue el sucesor elegido en consultas
  posteriores; no retorna automáticamente a los pesos de la instalación anterior.
- Errores de red, acceso, HTTP 404 ambiguo o paginación inválida son query-failed,
  conservando observación/plan previos como históricos. Inventario completo sin
  selección ni candidatos permite orphaned; evidencia insuficiente requiere intervención.
- Escritura atómica por unidad con comparación del registro de partida: un cambio
  concurrente no se sobrescribe. El fallo de una unidad no impide tratar las restantes.
- Update no cambia IDs, nombres/rutas locales, pesos, presets ni integridad; no crea
  operaciones de descarga. Aplicación física de planes corresponde a 031.

### Validación ejecutada

- Suite específica administrada final: 48 comprobaciones, exit 0;
  artifacts/task030-checks/model-update-managed.txt.
- Regresión administrada durante implementación: exit 0; managed-tests.txt.
- CLI y runner finales publicados Native AOT win-x64 sin advertencias, con errores
  estrictos de AOT/trimming; publish-product.txt y publish-runner.txt.
- Suite específica nativa final: 49 comprobaciones, exit 0 y stderr vacío;
  model-update-tests.txt y model-update-errors.txt. Incluye ejecución del CLI publicado.
- Regresión completa con runner y CLI nativos finales: exit 0, stderr vacío;
  native-tests.txt y native-errors.txt. Incluye contratos, procesos, almacenamiento,
  backends, transferencias, HF/GGUF, instalación, externos e integridad local.
- Documentación: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales válidos;
  docs.txt. git diff --check sin errores.
- Fixtures HTTP/GGUF, bytes locales testigo y homes temporales; sin consultas HF reales,
  pesos reales, servidores ni inferencia. No certifica compatibilidad ni integridad local.

### Reproducción

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --model-update-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task030-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task030-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task030-checks/run-native.ps1 -Filter '--model-update-tests' -OutputName 'model-update'
& artifacts/task030-checks/run-native.ps1 -OutputName 'native'
& artifacts/task030-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en artifacts/task030-checks/. El script nativo conserva el
entorno y normaliza Path/PATH al iniciar procesos de prueba.
