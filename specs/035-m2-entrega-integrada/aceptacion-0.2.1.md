# Recorrido de aceptación 0.2.1 con a4lg

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Base y alcance

Autorización: repetir la retirada, cascadas, reset, descargas, tres presets e
inferencia; entregar 0.2.1 sólo si no aparecen defectos, o elaborar un reporte.
Snapshot previo solicitado: `39783ae`, incluyendo la corrección y evidencia de
[040](../040-m2-reprueba-mtp-publicacion-cuda/README.md). Producto Native AOT win-x64 publicado desde ese
commit, aún con número interno 0.2.0 durante la prueba.

Evidencia local: `${CoquenaHome}/validation/0.2.1-final`; home real `${CoquenaHome}`.
Los comandos del recorrido están en `commands.txt` y transcripciones separadas.
No se consultó legacy ni se editaron directamente los catálogos del producto.

## Retirada y reconstrucción

- Rechazo de retirada de backend, principal, companion y servidor en uso.
- Detención normal de ornith; ausencia de instancias y descargas anteriores.
- Dry-run de retirada de parámetro y reset sin cambios en hashes de catálogos.
- Cancelación de cascada sin cambios; confirmación retira definición y binding,
  conservando preset y archivo del companion.
- Reset cancelado sin cambios y confirmado correctamente.
- Registro y desregistro de tres externos originales; hashes SHA-256 antes y
  después iguales, archivos conservados y registros ausentes.
- Retirada alternada de presets/modelos/companion; referencias rotas visibles y
  arranque rechazado, sin sustitución implícita. Backend retirado y después
  servidor detenido retirado, conservando logs.
- Snapshot de limpieza en `after-clean`; vecinos personales conservados.
- CUDA 12 oficial `b11714` instalado en primera invocación, sin resume.
  Backend `93a5823e-62a2-4b68-8736-40736d90946a`.
- Descargas simultáneas de Ornith 9B/35B Q4_K_M y a4lg MTP-ONLY Q8_0;
  selector a4lg ofrece las cinco cuantizaciones y permite elegir Q8_0.
- Pausa explícita de 9B y reanudación del mismo GUID; checkpoint guardado en
  `paused-9b.json`. Terminó correctamente, sin stderr ni operaciones pendientes.

Las definiciones y soporte local de parámetros se restauraron por CLI para
b11714. Servidor nuevo `ornith`, puerto 9931, `models-max=1`; la selección global
de backend se conserva.

## Inferencia y aceptación manual

Tres presets con nombres canónicos: `Ornith-1.5-9B`, `Ornith-1.5-35B-A3B` y
`Ornith-1.5-35B-A3B-MTP`. El último se creó mediante clone del base y binding
por identidad al nuevo a4lg. Se mantuvieron los valores de la receta documentada
en [la prueba inicial](prueba-ornith.md): ocho hilos, contexto 262144, parallel 1,
cachés Q8_0 y muestreo 0.6/0.95/20. Los MoE conservan n-cpu-moe 32; MTP,
draft-mtp con profundidad 3 y ocho hilos de draft. El 9B conserva ngram-mod.
`parameter-support-check.txt` comprueba definiciones y soporte de b11714;
`final-presets.ini` muestra valores y ruta efectiva del nuevo companion.

Los tres pedidos de código terminaron con stop y produjeron la función Python
add(a,b) esperada. Tras inspeccionarla, se ejecutaron tres asserts por preset:
nueve correctos. Cinco pedidos de herramienta terminaron con tool_calls, una
read_file y JSON válido con path src/Program.cs. No se ejecutó la herramienta.
Alternancia MTP → base → 9B → MTP correcta, mediante el mismo router.
Requests/responses y scripts/asserts están en `{9b,35b-base,35b-mtp}-*`;
las repeticiones de router, en `{9b,35b-mtp}-router-tool-*`.

El log confirma carga del draft a4lg separado, ocho hilos, un slot de 262144 y
aceptaciones draft 24/54, 22/87 y 23/72. No se llenó el contexto ni se hizo un
benchmark controlado: estos datos prueban funcionamiento, no optimalidad,
aceleración general o calidad suficiente para un arnés de programación completo.
Las advertencias del backend sobre auto-fit y mmap con expertos explícitos en
CPU se conservaron; la carga y las consultas terminaron correctamente con la
colocación elegida. No se cambió la política funcional para ocultarlas.

Backend, principal, companion y servidor en uso rechazaron su retirada con el
GUID activo. No quedaron operaciones de descarga. Instancia observada
`4df98b1a-04b2-44d9-b518-0f4734997cbf`, PID 32808, `127.0.0.1:9931/v1`;
MTP cargado y los otros dos unloaded, conforme a models-max 1. Estado temporal,
no garantía de permanencia. Snapshots en after-rebuild, final-instance.json,
final-models.json, final-presets.ini, final-server.log y final-server-*.txt.

No se detectaron nuevos defectos del producto en este recorrido. Se cumple la
condición del usuario para preparar 0.2.1. La causa histórica del fallo CUDA
sigue sin atribuirse retrospectivamente; la corrección del mecanismo reproducido
y sus límites están en 040. No se incorporan reglas funcionales nuevas en esta
aceptación ni funciones anticipadas de M3.

## Paquete candidato

Versión actualizada a 0.2.1 y snapshot de aceptación en `925a234`.
Publicación Native AOT de producto y runner win-x64 con advertencias AOT como
errores y análisis de trimming sin supresión. ZIP
`coquena-0.2.1-win-x64.zip`, 5 095 592 bytes, con coquena.exe, README.md y LICENSE.
Sin PDB, runner, backend ni pesos. SHA-256 ZIP:
`66d24d1ccb825b75636edd85b760467f4eed6d1ffaa46427b87365fcf9cbe8cc`.
SHA-256 ejecutable:
`c95669cc52150c166758625253260ce64ab19ba08b5252b51d81f855195a006c`.

La copia extraída coincide en hash e informa
`0.2.1+925a234e5772c5a9a6332b3fccfc79f08fbb981f`. Ayuda ejecutada correctamente.
`release-publish.txt`, `release-publish-runner.txt`, `release-hashes.json`,
`SHA256SUMS.txt` y `release-help.txt` conservan la procedencia.
La suite completa sobre la copia extraída terminó con exit 0 y stderr vacío:
`release-full-native/native-tests.txt` y `native-errors.txt`. Incluye los cinco
escenarios de contención/caída, 43 comprobaciones de publicación con lectores
Windows, 103 de resolución MTP y el conjunto general de almacenamiento,
parámetros, runtime, backends, modelos, companions e integridad/actualizaciones.
Los hashes de los cinco catálogos personales y config.json quedaron iguales
antes y después de la suite. Enlaces locales y JSON de evidencia válidos;
git diff --check correcto. No se modificaron diagramas PlantUML.

Reproducción desde la raíz del checkout:

```powershell
& specs/035-m2-entrega-integrada/run-native-tests.ps1 `
  -Executable ${CoquenaHome}/validation/0.2.1-final/release-extracted/coquena.exe `
  -Runner ${CoquenaHome}/validation/0.2.1-final/release-runner/Coquena.Tests.exe `
  -OutputDirectory ${CoquenaHome}/validation/0.2.1-final/release-full-native `
  -TempDirectory ${CoquenaHome}/validation/0.2.1-final/release-temp
```

Los commits posteriores de cierre/evidencia sólo cambian documentación y estado
LeanSpec; la procedencia del binario sigue siendo 925a234. Distribución prevista
en el [release v0.2.1](https://github.com/mstranich/coquena/releases/tag/v0.2.1),
con ZIP y SHA256SUMS.txt. Sin publicación de paquetes en NuGet.

El binario extraído inició de nuevo ornith y pasó una sexta llamada de
herramienta con MTP: `release-mtp-tool-{request,response}.json`. Estado dejado
activo: instancia `0e12855a-95a1-460a-896f-f807a4c85e61`, PID observado 51328,
con MTP cargado y otros presets unloaded; `release-instance.json`,
`release-models.json` y `release-server-ps.txt`. Continúa en 127.0.0.1:9931/v1.
La CLI instalada globalmente no se reemplazó: la prueba usa el ejecutable
Native AOT extraído del paquete indicado, sin confundirlo con una instalación vieja.
