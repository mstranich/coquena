---
status: complete
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:31.646847900Z
updated_at: 2026-10-17T15:00:57.304119600Z
completed_at: 2026-10-17T15:00:57.304119600Z
transitions:
- status: in-progress
  at: 2026-10-17T09:03:16.302055600Z
- status: complete
  at: 2026-10-17T15:00:57.304119600Z
---
# Comparar rutas Windows correctamente en las pruebas de runtime

Las referencias Git de Coquena de este documento apuntan a sus equivalentes en el historial saneado. Los hashes de modelos y backends externos se conservan.

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Problema y evidencia

La suite nativa de la prueba Ornith falló en "external start uses registered
executable and ID" al utilizar ${CoquenaHome} en las rutas de validación. Al usar
la capitalización real ${CoquenaHomeLower} superó esa comprobación. Se observan comparaciones
textuales sensibles a mayúsculas en ServerRuntimeTests para ExecutablePath, tanto
en el backend externo como en el trasladado.

La verificación del proceso en WindowsServerProcess ya compara rutas ignorando
capitalización. La evidencia apunta a un falso negativo del test; no demuestra
una identidad equivocada del proceso real. Confirmar los operandos al reproducir.

## Objetivo y alcance

Corregir las comparaciones de rutas de las pruebas para el destino Windows x64,
manteniendo por separado las comprobaciones estrictas de GUID e identidad del
proceso. No debilitar protecciones de producción ni extender soporte de plataformas.
Limitar la revisión a aserciones equivalentes afectadas, sin refactorización general.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Servidores](../../docs/servidores.md).
- [Evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: tests/Coquena.Tests/ServerRuntimeTests.cs; comparación de referencia
  en src/Coquena/Servers/WindowsServerProcess.cs.
- Antecedente completo: 035. La corrección de codificación es independiente.

## Trabajo y aceptación

- [x] Reproducir y registrar ambos operandos con rutas equivalentes de distinta
  capitalización, usando un home temporal aislado.
- [x] Corregir las aserciones para que la misma ruta Windows no falle por capitalización,
  conservando el rechazo de rutas distintas y BackendId incorrectos.
- [x] Cubrir backend externo y backend trasladado; no exigir renombrar carpetas reales.
- [x] Ejecutar pruebas pertinentes con capitalizaciones distintas y con la ruta real.
- [x] Publicar y ejecutar producto/runner Native AOT win-x64; evidencia y temporales en D:.
- [x] Registrar resultados y límites. Presentar para revisión, sin corregir producción,
  cerrar, commitear o publicar por la mera creación de esta tarjeta.

## Implementación y evidencia — 17 de octubre de 2026

Las dos aserciones de `ServerRuntimeTests` usan un helper local que compara
ExecutablePath con `StringComparison.OrdinalIgnoreCase` y BackendId por igualdad
estricta. En cada caso se comprueba la aceptación de variantes en mayúsculas y
minúsculas y el rechazo de otra ruta y otro GUID. Se registran ambos operandos.
No se modificaron producción, paquetes ni reglas funcionales. La política coincide
con WindowsServerProcess y no anticipa soporte multiplataforma ni cambios de M3.

Evidencia en `${CoquenaHomeLower}/validation/038`:

- `reproduce.txt`: fallo anterior confirmado; ruta observada `${CoquenaHomeLower}/...`
  frente a ruta registrada `${CoquenaHome}/...`, con el resto del path idéntico.
- `build.txt`, `managed-mixed.txt` y `managed-real.txt`: compilación y runtime
  administrado, exit 0 en ambas capitalizaciones; 92 checks de ciclo y 22 de
  métricas/retención por ejecución, con homes temporales aislados.
- `publish-product.txt`, `publish-runner.txt`: producto y runner Native AOT win-x64,
  exit 0, con IlcTreatWarningsAsErrors=true, TrimmerSingleWarn=false y
  SuppressTrimAnalysisWarnings=false; runner con PublishAot=true.
- `native-direct-mixed.txt` y `native-direct-real.txt`: runtime con runner y CLI
  nativos invocados directamente desde PowerShell, TEMP/TMP en D:, página 65001 y
  Console.OutputEncoding UTF-8. Los archivos `*-errors.txt` conservan stderr.
- `native-mixed` y `native-mixed-utf8`: intentos mediante el helper de 035;
  pasan la aserción externa pero se detienen en `recent logs bounded and UTF8 intact`.
  Configurar chcp y Console.Encoding en el padre no corrige ese camino sin ventana.
  Es evidencia del problema independiente 039, que no se modificó aquí.

Reproducción de las pasadas nativas exitosas (PowerShell):

```powershell
chcp 65001 > $null
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$env:TEMP = "${CoquenaHome}/validation/038/temp" # repetir con ${CoquenaHomeLower}
$env:TMP = $env:TEMP
& ${CoquenaHomeLower}/validation/038/runner/Coquena.Tests.exe --runtime-tests ${CoquenaHomeLower}/validation/038/product/coquena.exe
```

No se renombraron directorios ni se usó el home personal. No se ejecutó inferencia
ni se descargaron recursos reales. Se ejecutó la suite completa de runtime, no la
suite general de todos los módulos. La identidad real del proceso conserva sus
pruebas existentes de PID reutilizado, ruta ajena, creación y pertenencia al job.

`leanspec deps 038` informa que no está migrado al adaptador; se comprobaron
manualmente la tarjeta, board y M2: sin bloqueos entre 036–039. Documentación
funcional y diagramas sin cambios. Tarjeta en in-progress para revisión; sin cierre,
commit ni publicación de release.
Resultado final nativo: ambas capitalizaciones exit 0, 92 comprobaciones de runtime
y 22 de métricas/retención por pasada; stderr vacío en ambas. Enlaces locales de la
tarjeta y git diff --check verificados.

## Revisión independiente — 17 de octubre de 2026

Revisión del diff contra `89c15a0`: sólo cambian las dos aserciones de rutas,
su helper local y esta tarjeta. La comparación coincide con `WindowsServerProcess`;
conserva GUID estricto y no modifica la verificación de creación, ejecutable o job
del proceso. Sin nuevos hallazgos bloqueantes ni cambios de reglas funcionales.

Evidencia propia en `${CoquenaHomeLower}/validation/038-review`: build Release sin errores
ni advertencias; publicación nueva de producto y runner Native AOT win-x64 con
las opciones estrictas indicadas arriba. Cuatro ejecuciones exitosas: administrada
y nativa, cada una con TEMP/TMP en `${CoquenaHome}` y `${CoquenaHomeLower}`. Cada pasada completó
92 comprobaciones de runtime y 22 de métricas/retención, exit 0 y stderr vacío.
Los logs confirman la diferencia real de capitalización entre ruta esperada y
observada para el backend externo y el trasladado. Se verificaron enlaces locales
y `git diff --check`.

Límite: ejecución directa con página 65001 y Console.OutputEncoding UTF-8; no
certifica resuelta la sensibilidad de codificación registrada en 039. No se ejecutó
la suite general, inferencia ni descargas. El home personal no se modificó.
Se mantiene in-progress para aprobación, sin cierre, commit ni publicación.


## Aprobación y cierre

El usuario aprobó la entrega el 17 de octubre de 2026 y autorizó cerrar la tarea
y realizar un único commit con la corrección, pruebas y evidencia documentada,
incluida la verificación independiente. Se marca complete mediante LeanSpec.
Sin publicación de release ni push.
