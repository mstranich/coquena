---
status: complete
created: 2026-10-17
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-17T16:12:21.399365500Z
updated_at: 2026-10-17T17:07:06.555409700Z
completed_at: 2026-10-17T17:07:06.555409700Z
transitions:
- status: in-progress
  at: 2026-10-17T16:12:55.999616Z
- status: complete
  at: 2026-10-17T17:07:06.555409700Z
---
# Contemplar MTP-ONLY y resolver el fallo de publicación CUDA observado en la reprueba

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Problema y autorización

El usuario solicitó convertir el [reporte de la reprueba](../035-m2-entrega-integrada/reporte-reprueba-0.2.1.md)
en una única tarea e implementarla hasta completar el trabajo. El selector excluye
el companion a4lg `MTP-ONLY`, ya utilizado en la prueba anterior. La instalación
CUDA 12 b11712 falló en publishing con `transfer-io-failed`; resume la recuperó.
No se conoce aún la causa de ese segundo fallo y no se presupone contención.

## Alcance

Reconocer el marcador explícito delimitado `MTP-ONLY` como candidato auxiliar,
conservando principales con MTP interno, conflictos de metadata y evidencia candidata.
Reproducir/localizar el fallo de publicación del backend, corregir el mecanismo
comprobado y conservar recuperación, cancelación, exclusión y validación. Añadir
diagnósticos seguros cuando hagan falta para distinguir fallos sin exponer secretos.
No convertir toda E/S en reintentos ni presentar un workaround como causa resuelta.
Sin nuevos paquetes, migraciones, certificación de compatibilidad o anticipación de M3.
Commit/tag/release no forman parte de esta tarea de corrección.

## Referencias y dependencias

- [M2](../../docs/milestones/M2-gestion-completa.md), [M3](../../docs/milestones/M3-definiciones-declarativas.md).
- [Modelos y companions](../../docs/modelos-y-companions.md), [descargas](../../docs/descargas.md),
  [servidores](../../docs/servidores.md), [CLI](../../docs/comandos.md).
- Antecedentes terminados: 036 y 037; no son bloqueos abiertos.
- [Recorrido y evidencia](../035-m2-entrega-integrada/reprueba-0.2.1.md).
- Estado local real: `${CoquenaHome}`; validar preferentemente en homes aislados en D:.

## Trabajo y aceptación

- [x] Fijar inventario a4lg y cubrir selección/instalación MTP-ONLY y negativos.
- [x] Localizar el fallo de publicación CUDA con evidencia reproducible y distinguir
  lo demostrado del incidente histórico cuya excepción no quedó registrada.
- [x] Corregir el mecanismo comprobado y cubrir recuperación, errores persistentes,
  cancelación y ausencia de publicación parcial sin relajar identidad/integridad.
- [x] Actualizar la regla canónica de marcadores y cualquier regla funcional que cambie,
  explicando expresamente decisiones nuevas y límites.
- [x] Ejecutar pruebas pertinentes, publicar producto/runner Native AOT win-x64 y
  ejecutar sus binarios; evidencia en D:, sin alterar recursos personales ajenos.
- [x] Revisar enlaces, JSON/diagramas afectados y git diff --check; entregar reporte
  de hallazgos, límites y resultados de la tarea única.

## Investigación y correcciones

### Clasificación MTP-ONLY

Se amplía la regla canónica de marcadores por la solicitud explícita del usuario:
`mtp-only`, delimitado como los marcadores de draft existentes, produce un
candidato auxiliar. Un MTP interno aislado sigue sin cambiar el rol principal.
No se certifican rol/compatibilidad por nombre, tamaño o `general.type=model`;
se conservan conflictos de cuantización, arquitectura y shards. No se migran ni
reclasifican automáticamente registros ya instalados.

Fixture con las cinco rutas/tamaños a4lg del commit
`034793caf3d6f3a60cd9f0c37765434be9931cf4`; las pruebas de instalación usan un
GGUF sintético pequeño y fijan ese commit explícitamente. Incluyen selección
con/sin cuantización, cancelación sin operación, persistencia MTP y límites de
token, mayúsculas, metadata conflictiva y shards. No se usa una excepción por
publicador ni se vuelve a aceptar cualquier token MTP.

### Publicación de backend y diagnóstico

No se pudo recuperar la excepción histórica: el JSON sólo conservaba el código
genérico. Cincuenta sondeos `--version` seguidos de movimiento inmediato y seis
instalaciones con los archivos reales b11712 recién extraídos pasaron sin fallos
espontáneos. La primera sonda de ZIP incluía por error el marcador reservado del
backend instalado; se corrigió el fixture antes de contar esas seis instalaciones.

Se comprobó un mecanismo concreto en Windows: un lector de un archivo hijo sin
FileShare.Delete hace fallar el renombrado del directorio con IOException,
HRESULT `0x80070005`. Liberarlo permite moverlo. Una sonda con el backend real,
su `--version` nativo y un lector controlado de 250 ms reprodujo con la DLL
anterior `failed/publishing/transfer-io-failed`; resume completó sin descargar.
La misma sonda enlazada a la DLL corregida terminó en la primera invocación.
Esto reproduce fase, código genérico y recuperación del síntoma; no demuestra
que ese lector fuera la causa del incidente histórico ni identifica un antivirus.

La implementación trata sólo errores Windows 5/32/33 del movimiento concreto:
ventana de reintentos de un segundo, esperas cancelables de 25 ms fuera de la
exclusión global, sin red nueva. La versión preparada y su propietario vuelven
a comprobarse; cada commit revalida catálogo, selección, plan y uso activo.
Una copia completa de otra raíz se conserva y verifica antes de reintentar su
renombrado; una copia incompleta propia se reconstruye. Persistir el conflicto
produce `backend-publication-move-0x< HRESULT >` (sin espacios), conserva el GUID
y permite resume/cancelación, sin texto de excepción ni rutas sensibles.

La documentación de descargas incorpora explícitamente esta espera acotada como
comportamiento nuevo, compatible con la reanudación manual tras un fallo final.
No se reintentan otras operaciones de E/S ni se reemplaza un destino ajeno. Un
error 5 también puede corresponder a permisos persistentes: agotada la ventana
se informa, no se cambian ACL ni se sigue esperando indefinidamente.

## Evidencia y reproducción

Raíz local `${CoquenaHome}/validation/040`; homes aislados y temporales en D:.
S: y D: son almacenamiento físico compartido: las pruebas de copia ejercitan
raíces lógicas distintas, no acreditan discos independientes.

- `probe-before.txt`: 50 sondeos/movimientos sin conflicto espontáneo.
- `install-before-payload.txt`: seis instalaciones completas con archivos b11712,
  ZIP local y sondeo nativo real. Transporte local, sin resolver otra versión.
- `locked-move-before.txt`: denegación real de Windows al mantener el lector.
- `baseline/coquena.dll`, SHA-256
  `A6121917433E6B91824A3529594EF5F9D81EA8243561D88402BB013580173FF0`,
  conservada antes de cambiar BackendInstaller; sólo tenía adelantado el cambio
  del marcador MTP. `blocked-before.txt` y `blocked-after.txt` contienen el antes/
  después controlado con esa DLL y la corregida; sus scripts/proyectos están en
  `probe` y `blocked-probe`. Se registra tipo/HRESULT/stack sin texto sensible.
- `backend-managed.txt`: 53 comprobaciones de instalador y 43 de publicación.
- `backend-directory-managed.txt`: 98 comprobaciones, incluida copia verificada
  que reintenta el renombrado sin recopiado y permite otro escritor del home.
- `mtp-managed.txt`: primera regresión, 217 existentes y 99 MTP; después se
  añadieron dos comprobaciones de shards, cubiertas por la pasada nativa final.
- `publish-product.txt`, `publish-runner.txt`: producto y runner Native AOT
  win-x64, con IlcTreatWarningsAsErrors=true, TrimmerSingleWarn=false,
  SuppressTrimAnalysisWarnings=false y PublishAot=true en el runner.
- `native-backend`: 57 comprobaciones de instalador y 43 de publicación.
- `native-mtp`: 217 existentes y 103 MTP, incluida consulta nativa de catálogos.
- `native-directories`: 105 comprobaciones. Las tres pasadas exit 0 y stderr vacío.
- `full-native/native-tests.txt` y `native-errors.txt`: suite general completa
  con producto/runner Native AOT nuevos, ejecutada sola; exit 0 y stderr vacío.
  Incluye almacenamiento, runtime, controles de descarga/contención, backends,
  modelos/companions, actualizaciones e integridad, además de las nuevas regresiones.
- `real-a4lg-download.txt`: selector interactivo del producto Native AOT contra
  Hugging Face; descarga e instalación reales Q8_0, 1 990 649 632 bytes,
  commit anterior fijado, SHA-256
  `f6cb995fab78e7284097b2e13508e8daad4ffc6a747b433aef399f4e48524b5a`.
- `real-cuda-install.txt`, `real-cuda-errors.txt`: CLI Native AOT, instalación
  oficial CUDA 12 b11712 en primera invocación exit 0, sin resume ni stderr;
  iniciada con el companion en el mismo home aislado. Ambos catálogos disponibles
  y sin operaciones pendientes en `real-a4lg-home`.
- `source-hashes.json`, `binary-hashes.json`: fuentes y ejecutables verificados.
- `personal-home-check.txt`: los cinco catálogos personales siguen idénticos al
  snapshot final de la reprueba. El servidor personal no se detuvo ni reconfiguró.

Regresiones dirigidas nativas desde la raíz del checkout, con el helper de 035:

```powershell
& specs/035-m2-entrega-integrada/run-native-tests.ps1 `
  -Executable ${CoquenaHome}/validation/040/product/coquena.exe `
  -Runner ${CoquenaHome}/validation/040/runner/Coquena.Tests.exe `
  -Filter --backend-publication-tests `
  -OutputDirectory ${CoquenaHome}/validation/040/native-backend `
  -TempDirectory ${CoquenaHome}/validation/040/temp
# Repetir con --mtp-tests y --backend-directory-tests, en sus carpetas de salida.
# Suite general: omitir -Filter y usar la carpeta full-native.
```

## Hallazgos y límites para revisión

La evidencia prueba la exclusión y corrección de MTP-ONLY, así como un fallo de
renombrado recuperable que antes exigía resume. No prueba retrospectivamente
la causa del fallo inicial CUDA; ese límite se conserva en la entrega. El HRESULT
seguro permite distinguir futuros fallos en el movimiento, sin inventar su causa.
No se promete corregir todo transfer-io-failed ni problemas de permisos persistentes.

La espera acotada no retiene el bloqueo global, pero la copia/verificación de árboles
grandes conserva su mecanismo anterior; no se ensayó aquí carga sostenida de copias
grandes entre raíces concurrentes. Es un límite de escalabilidad relevante para
M3/una interfaz operativa, no una función que se anticipe ni otra tarjeta creada.
La selección MTP sigue siendo una heurística visible: poder descargar no certifica
aceleración. Esta corrección no repite el benchmark de inferencia ni llena el contexto.

`leanspec deps` no está implementado por el adaptador; se revisan manualmente
board/search/view, antecedentes terminados y enlaces al milestone. No hay bloqueos
abiertos que requieran una dependencia nueva. No cambian JSON de producto,
esquema persistido, diagramas, paquetes, versión ni contrato de modelos externos.

Validación terminada: enlaces locales de los documentos afectados, JSON de evidencia
y catálogos reales, hashes de fuentes publicadas y `git diff --check` correctos.
No se modificaron diagramas PlantUML ni ejemplos JSON independientes de producto.
Se revisó el diff de producción y pruebas contra los criterios de la tarjeta;
se conserva la limitación de atribución histórica, sin presentar la instalación
real exitosa como prueba de causa del fallo anterior.

La implementación se entregó inicialmente en in-progress para revisión humana.
El usuario autorizó después el commit previo al nuevo recorrido: `39783ae`
incluye esta corrección y la evidencia histórica. Su instrucción de concluir
0.2.1 si el recorrido no encontraba más defectos se cumplió con la
[aceptación completa](../035-m2-entrega-integrada/aceptacion-0.2.1.md), incluida
la suite Native AOT sobre el paquete extraído. Se cierra la tarjeta mediante
LeanSpec; versión y publicación se registran en esa aceptación, fuera del
alcance de implementación de esta corrección. No se atribuye retrospectivamente
la causa histórica ni se crean nuevas tareas o reglas funcionales durante la prueba.
