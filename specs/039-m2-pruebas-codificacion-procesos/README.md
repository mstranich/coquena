---
status: complete
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:32.586730500Z
updated_at: 2026-10-17T15:15:45.913506Z
completed_at: 2026-10-17T15:15:45.913506Z
transitions:
- status: in-progress
  at: 2026-10-17T15:03:09.356273200Z
- status: complete
  at: 2026-10-17T15:15:45.913506Z
---
# Eliminar dependencia de la página de códigos en las pruebas de procesos

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Problema y evidencia

La prueba "recent logs bounded and UTF8 intact" falló con la consola original y
la suite completa pasó tras ejecutar chcp 65001. El caso escribe "recent UTF8 á fin"
y comprueba su lectura vía CLI. La captura de procesos en ServerRuntimeTests y el
helper run-native-tests.ps1 no fijan explícitamente la codificación de las salidas.

Está comprobada la sensibilidad del procedimiento al entorno; aún hay que aislar
si la divergencia está en la emisión de la CLI, la decodificación de procesos, la
captura del runner o varias de ellas. No atribuir corrupción al archivo de log sin
comprobar sus bytes. La comparación de rutas de 038 es otro defecto independiente.

## Objetivo y alcance

Hacer reproducible la comprobación de logs y diagnósticos con caracteres no ASCII
sin exigir modificar manualmente la página de códigos global. Definir y comprobar
el contrato entre productor y lector en los puntos afectados. Comenzar por el
harness de pruebas; cualquier cambio observable a la codificación de la CLI debe
justificarse contra la documentación y consultarse si queda una decisión funcional.

Conservar la comprobación de integridad textual, límites del log y códigos de salida.
No resolver el fallo eliminando acentos ni relajando la aserción a texto ASCII.
No rediseñar logging, traducciones ni comandos.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Servidores y logs](../../docs/servidores.md), [CLI](../../docs/comandos.md).
- [Evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: tests/Coquena.Tests/ServerRuntimeTests.cs y
  specs/035-m2-entrega-integrada/run-native-tests.ps1; inspeccionar los productores
  concretos de salida antes de decidir una corrección adicional.
- Antecedentes completos: 013 y 035. Sin bloqueo de 038 para un caso enfocado.

## Trabajo y aceptación

- [x] Reproducir en consola UTF-8 y al menos una página de códigos Windows distinta,
  registrando bytes del log, bytes emitidos y texto capturado para localizar la divergencia.
- [x] Garantizar que la captura y su productor usan un contrato compatible sin depender
  de chcp global; conservar stdout y stderr y diagnósticos no ASCII.
- [x] Probar una cadena con acentos y caracteres fuera de la página de códigos elegida,
  así como el límite de lectura reciente del log.
- [x] Ejecutar la regresión enfocada en ambos entornos y la suite pertinente; no
  presentar normalización externa como una corrección del código.
- [x] Publicar y ejecutar producto/runner Native AOT win-x64, con temporales en D:.
- [x] Registrar decisiones, posibles cambios funcionales y evidencia. Requiere revisión;
  crear esta tarjeta no autoriza implementación, cierre, commit ni publicación.

## Implementación y evidencia — 17 de octubre de 2026

La instrucción del usuario de tomar esta tarea autoriza su implementación. Se mantiene
in-progress para revisión, sin cierre, commit, push ni publicación de release.

### Causa comprobada y decisión

La CLI ya fija `Console.OutputEncoding` a UTF-8 sin BOM en `Program.cs`; no se
modificó producción. En cambio, los lectores del test y del helper de 035 usaban
la codificación predeterminada. Un lector ejecutado sin consola eligió página 850
para bytes que el producto emitió correctamente en UTF-8. La divergencia está en
la decodificación, no en los bytes del log ni en la emisión de la CLI.

`producer-probe-nowindow.json` registra un diagnóstico del producto nativo anterior:
exit 1, lector predeterminado 850, stderr crudo UTF-8 y las dos decodificaciones.
`á漢字😀` conserva bytes `C3A1E6BCA2E5AD97F09F9880`; interpretarlos con 850 altera
el texto. La sonda con consola (`producer-probe.txt`) eligió 65001: confirma que
el valor predeterminado depende del entorno del lector. No se cambió chcp global.

La captura local de `ServerRuntimeTests` ahora drena ambos pipes en paralelo,
conserva los bytes y decodifica UTF-8 estrictamente. Esto permite comprobar bytes
y texto por separado, además de rechazar secuencias inválidas. El runner fija
UTF-8 sin BOM para stdout y stderr, como la CLI. El helper de 035 decodifica ambos
canales explícitamente con UTF-8 estricto; conserva sus archivos y códigos de salida.

El caso escribe `recent UTF8 á 漢字 😀 fin` después de una línea de 70 000 caracteres.
Comprueba bytes del archivo, bytes emitidos, texto intacto y máximo de 65 536 bytes
de salida reciente. Otra invocación comprueba un error con el mismo texto Unicode
en stderr y salida fallida. No se eliminaron acentos ni se relajó la aserción.

Las reglas de consulta reciente de `docs/servidores.md` y la sintaxis de
`docs/comandos.md` se mantienen. No se añadió una política funcional ni se modificó
documentación funcional, JSON, diagramas, dependencias o paquetes. No se anticipan
funciones de M3: el contrato fijado aquí corresponde a la captura del ejecutable
actual en Windows, no define codificación de YAML ni compatibilidad de otros runtimes.

### Validación reproducible

Evidencia local en `${CoquenaHomeLower}/validation/039`:

- `before` y `before-matrix/{437,65001}`: runner/producto nativos anteriores y helper
  original; falla `recent logs bounded and UTF8 intact` en ambas páginas. Cambiar
  la página del padre no arregla la captura interna del runner sin consola.
- `build.txt` y `managed`: compilación Release sin advertencias/errores y ejecución
  enfocada exit 0, 95 comprobaciones de runtime y 22 de métricas/retención.
- `publish-product.txt` y `publish-runner.txt`: publicación nueva Native AOT win-x64,
  con `IlcTreatWarningsAsErrors=true`, `TrimmerSingleWarn=false` y
  `SuppressTrimAnalysisWarnings=false`; runner con `PublishAot=true`.
- `native-matrix/{437,65001}` y `managed-matrix/{437,65001}`: cuatro pasadas
  enfocadas exit 0, cada una con 95 comprobaciones de runtime y 22 de
  métricas/retención, stderr vacío. Cada salida registra bytes del log/stdout y
  texto capturado; `codepage.txt` registra la página usada en la consola aislada.
- `full-suite/native-tests.txt` y `native-errors.txt`: suite general completa con
  runner y producto Native AOT recién publicados, exit 0 y stderr vacío. Incluye
  contratos, CLI, runtime, almacenamiento, descargas/contención, backends,
  resolución MTP, modelos y actualizaciones; no solamente el filtro de runtime.

El [script de matriz](run-codepages.ps1) inicia una consola oculta independiente
por página y llama al helper corregido. Chcp afecta solamente esas consolas.
Ejemplo desde la raíz del checkout (PowerShell 7):

```powershell
& specs/039-m2-pruebas-codificacion-procesos/run-codepages.ps1 `
  -Executable ${CoquenaHomeLower}/validation/039/product/coquena.exe `
  -Runner ${CoquenaHomeLower}/validation/039/runner/Coquena.Tests.exe `
  -OutputDirectory ${CoquenaHomeLower}/validation/039/native-matrix
```

Todos los homes y temporales de validación están aislados en D:. No se usó el
home personal, inferencia ni descargas de modelos reales. El adaptador no implementa
`leanspec deps`; se revisaron manualmente board, search, tarjeta y M2: sin bloqueos.
La corrección no certifica soporte multiplataforma ni la codificación de salidas
arbitrarias de backends externos. No se detectaron nuevas decisiones funcionales
pendientes ni hallazgos que requieran ampliar esta tarea.

Se comprobaron sintaxis de ambos scripts PowerShell, enlaces locales de esta
tarjeta y `git diff --check`. Estado final: in-progress, pendiente de revisión y
aprobación explícita; sin commit ni cierre.

## Revisión independiente — 17 de octubre de 2026

Diff revisado contra `4546731`: sin cambios en producción ni reglas funcionales.
La CLI ya establece UTF-8; los lectores ahora respetan ese contrato y conservan
bytes para distinguir emisión de decodificación. Se mantienen las comprobaciones
de Unicode, límite de salida reciente y código fallido del diagnóstico. Sin nuevos
hallazgos bloqueantes en la corrección.

Evidencia propia en `${CoquenaHomeLower}/validation/039-review`: compilación Release sin
errores ni advertencias y publicación nueva de producto/runner Native AOT win-x64
con las opciones estrictas indicadas arriba. Matrices `native-matrix` y
`managed-matrix`, páginas 437 y 65001: cuatro pasadas exit 0, cada una con 95
comprobaciones de runtime y 22 de métricas/retención, stderr vacío. Los bytes del
log y stdout conservan `á 漢字 😀` y coinciden con el texto capturado.

La primera suite general (`full-suite`), ejecutada simultáneamente con ambas
matrices, falló por `Runtime fixture timeout` después de las 95 comprobaciones,
en el tramo posterior de pruebas de primer plano. La repetición sin otras suites
de revisión en paralelo (`full-suite-retry`) terminó exit 0 y stderr vacío.
Se conserva el primer fallo: la concurrencia es una posible influencia, no una
causa demostrada. No se cambiaron timeouts ni pruebas de procesos para ocultarlo;
no se certifica estabilidad de suites simultáneas.

Sintaxis PowerShell de ambos scripts, enlaces locales y `git diff --check`
correctos. Homes y temporales aislados en D:, sin modificar el home personal.
Sin inferencia ni descargas reales. Tarjeta abierta para aprobación, sin cierre,
commit, push ni release.

## Aprobación y cierre

El usuario aprobó la entrega el 17 de octubre de 2026 y autorizó cerrar la tarea
y realizar un único commit con la corrección, pruebas y evidencia documentada,
incluida la revisión independiente y su limitación de estabilidad bajo ejecución
simultánea. Se marca complete mediante LeanSpec. Sin push ni publicación de release.
