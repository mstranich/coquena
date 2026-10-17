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
