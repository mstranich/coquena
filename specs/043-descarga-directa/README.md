---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.525824600Z
updated_at: 2026-10-22T17:08:15.382039400Z
completed_at: 2026-10-22T17:08:15.382039400Z
transitions:
- status: in-progress
  at: 2026-10-22T16:32:57.157172900Z
- status: complete
  at: 2026-10-22T17:08:15.382039400Z
---
# Descargar una cuantización explícita inequívoca sin interacción redundante

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo

Corregir el camino actual de descarga cuando el selector identifica una única unidad válida.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md#model](../../docs/comandos.md#model)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [descargas.md](../../docs/descargas.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [x] Reproducir con fixtures la interacción redundante para un selector como unsloth/gemma-4-12b-it-GGUF:Q4_0, sin descargar pesos costosos.
- [x] Descargar directamente una cuantización explícita con una unidad válida, sin menú, filtrado ni oferta de README.
- [x] Contar unidades, no archivos: un conjunto de shards coherente puede ser la única unidad válida.
- [x] Conservar selección ante varias unidades y diagnóstico ante ausencia o resultado no concluyente; nunca sustituir cuantización ni añadir companions implícitos.
- [x] Mantener integridad, validación GGUF, reanudación y el recorrido sin cuantización explícita.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Correctivo independiente sobre la base M2; no depende del compositor ni implementa recursos declarativos de proyecto.

## Estado y evidencia

Implementación terminada y aprobada el 2026-10-22. Tarjeta cerrada con un único commit que agrupa implementación, pruebas y evidencia.

### Implementación

- `ModelSelectionMenu` deriva la cuantización explícita con una única unidad al mismo validador GGUF usado por el menú. Sólo devuelve la selección fijada tras comprobar cabeceras, rol, etiqueta y coherencia split; no requiere terminal interactiva ni ofrece README.
- Varias unidades conservan la elección por rutas; sin cuantización permanece el menú. Ausencia e incertidumbre conservan diagnósticos sin sustituciones. Un fallo de validación en el camino directo se propaga antes de crear el GUID, sin volver a un menú redundante.
- La CLI y el instalador mantienen transferencia, integridad, reanudación y publicación existentes. El consentimiento para convertir un externo sigue siendo obligatorio. La misma selección compartida aplica a companions solicitados explícitamente, sin incorporar recursos implícitos.
- Sin nuevas dependencias ni modificaciones de documentación funcional: se implementa la regla ya definida en comandos y modelos/companions.

### Validación ejecutada

Temporales y publicaciones en `${TestRoot}`; fixtures HTTP y pesos sintéticos pequeños, sin descargar modelos reales ni utilizar la home de uso.

- Reproducción antes del cambio: `dotnet run --project tests/Coquena.Tests -c Release -- --model-selection-tests` falla con `interaction-required` ante una cuantización explícita única sin terminal. La misma regresión pasa después del cambio.
- Selección focalizada administrada y Native AOT: 183 comprobaciones HF/selección y 217 de resolución/metadatos/HTTP. Incluyen selección directa sin README, comparación sin distinguir mayúsculas, dos shards como unidad única, rechazo de split incoherente, varias rutas, cuantización ausente, menú sin cuantización y reanudación con publicación de bytes exactos.
- Instalación administrada: 305 comprobaciones. Contra el producto Native AOT: 314 comprobaciones tanto desde el ejecutor administrado como desde el ejecutor Native AOT. Incluyen descarga directa desde CLI, companions explícitos, consentimiento/rechazo/EOF en transición externa, integridad, recuperación y preservación del catálogo.
- `dotnet publish src/Coquena -c Release -r win-x64 -o ${TestRoot}/coquena-043/publish`: publicación Native AOT sin errores ni advertencias.
- `dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -o ${TestRoot}/coquena-043/native-tests`: ejecutor nativo publicado sin errores ni advertencias. Ejecutado con `--model-selection-tests` y `--model-install-tests ${TestRoot}/coquena-043/publish/coquena.exe`; ambos con salida 0.
- Ejecutado también `dotnet run --project tests/Coquena.Tests -c Release -- --model-install-tests ${TestRoot}/coquena-043/publish/coquena.exe`, salida 0.
- Revisión manual mediante board/search/view y tabla del milestone: sin bloqueos. `leanspec deps` no está migrado al adaptador; no se interpretó ese error como ausencia de dependencias.

### Evaluación para revisión

Sin hallazgos adicionales que requieran seguimiento. La descarga directa conserva el motor de instalación y no establece una política nueva para M3/M4. El consentimiento ante conversión de externos es una interacción distinta de la selección redundante y permanece probado. Un fallo de cabecera en el camino directo termina con diagnóstico sin operación: consecuencia de conservar la validación previa exigida, no una sustitución ni una nueva regla funcional.

La evidencia usa fixtures y no acredita disponibilidad actual del repositorio de ejemplo, inferencia ni recursos declarativos futuros. No se ejecutó la batería general completa. No se modificaron diagramas ni ejemplos JSON. Trabajo aprobado, cerrado y comprometido en un único commit.

### Corrección posterior de las regresiones MTP

El historial confirmó que `MtpResolutionTests` quedó desfasado al introducir esta tarea: sobre el padre de `ee7880d` pasaban sus 101 comprobaciones, mientras que en ese commit comenzó a fallar `037: one unit plus README/cancel`. La prueba exigía menú incluso para una cuantización explícita inequívoca; también intentaba cancelar ese recorrido mediante opciones que ya no se ofrecen.

Por autorización expresa del usuario se actualizaron únicamente esas pruebas, sin cambiar código de producto ni reglas funcionales. MTP y MTP-ONLY conservan menú, divulgación de candidatura y cancelación sin efectos cuando no se indica cuantización. Los selectores explícitos únicos se prueban con interacción deshabilitada y sin opciones de menú, conservando rol, commit, archivos y transferencia del único companion solicitado.

Validación con entorno de evidencia anonimizado y fixtures sintéticos locales, sin descargar pesos reales:

- Build Release correcto, sin advertencias ni errores.
- `--mtp-tests` administrado mediante apphost: 217 comprobaciones de resolución y 102 MTP, correctas.
- Publicación del runner Native AOT win-x64 con `TreatWarningsAsErrors=true`: correcta. `--mtp-tests` contra el producto Native AOT: 217 comprobaciones de resolución y 104 MTP, correctas. Los procesos M2 retenidos se ejercitan mediante el worker de API del runner.
- La publicación del producto de 049 se reutiliza porque esta corrección no modifica sus fuentes.
- Suite general completa con runner y producto Native AOT: salida 0, incluido MTP y las regresiones posteriores de GGUF, adquisición, directorios, externos, integridad, matching, update y upgrade. No se observaron hallazgos nuevos en este recorrido.
- Enlaces locales de la tarjeta y `git diff --check`: correctos. No se modificaron documentación funcional, diagramas, ejemplos JSON ni dependencias.
