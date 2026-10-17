---
status: complete
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:30.689473900Z
updated_at: 2026-10-17T08:58:39.601495300Z
completed_at: 2026-10-17T08:58:39.601495300Z
transitions:
- status: in-progress
  at: 2026-10-17T04:31:20.810248400Z
- status: complete
  at: 2026-10-17T08:58:39.601495300Z
---
# Distinguir drafts MTP de modelos principales con MTP

## Problema y evidencia

En EryriLabs/Ornith-1.5-35B-A3B-BigBang-MTP-GGUF, el selector #mtp:Q8_0
ofreció el principal Ornith-1.5-35B-A3B-BigBang-MTP-Q8_0.gguf (36.9 GB)
y omitió el draft separado mtpdraft-Q8_0.gguf (aproximadamente 2 GB).
Sin filtro de cuantización tampoco apareció ese draft. La selección se canceló:
no se descargó el principal equivocado.

ModelResolution reconoce mtp como token delimitado. El nombre de un principal con
MTP integrado o presente en su denominación puede confundirse con el rol auxiliar,
mientras mtpdraft no coincide. El companion alternativo de a4lg sí pudo instalarse.
Los nombres y el tamaño son pistas, no prueba suficiente de rol o compatibilidad.

## Objetivo y alcance

Corregir la resolución de candidatos principales/companions para reconocer drafts
MTP separados y no certificar un principal como companion sólo por contener MTP.
Reproducir con inventario y cabeceras fijados. Usar evidencia acotada de nombre,
carpeta y cabecera; representar incertidumbre o contradicción cuando no baste.
No resolverlo mediante una excepción para un publicador ni por un umbral de tamaño.

Si las cabeceras no permiten distinguir un caso, documentar la evidencia disponible
y acordar el tratamiento antes de inventar una garantía. Mantener selección explícita,
commit fijado, unidades y shards completos y ausencia de operaciones antes de confirmar.
No incorporar nuevos tipos de companion, convertir checkpoints ni certificar
compatibilidad de inferencia entre arquitecturas.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Modelos y companions](../../docs/modelos-y-companions.md).
- [CLI](../../docs/comandos.md), [evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: src/Coquena/Models/ModelResolution.cs y consumidores de selección.
- Inventario EryriLabs observado en commit 2e9f6f487b82200f2a045e67d01a3f1e2982b00a.
- Antecedentes completos: 015 y 027. Sin dependencia bloqueante con las otras correcciones.

## Trabajo y aceptación

- [x] Fixture reproduce la omisión de mtpdraft-Q8_0 y el falso candidato principal.
- [x] El draft separado aparece como candidato auxiliar con cuantización y evidencia
  justificadas; un principal con MTP no se ofrece como companion confirmado por su nombre.
- [x] Cubrir MTP integrado, separado, nombres arbitrarios, cabecera insuficiente,
  contradicciones, variantes de cuantización y shards; preservar mmproj y principales.
- [x] Conservar límites de red/metadatos, selecciones explícitas y cancelación sin GUID.
- [x] Probar selección e instalación del companion mediante CLI Native AOT win-x64
  con fixtures reproducibles y sin descargar los 36.9 GB del principal.
- [x] Actualizar documentación sólo si cambia o se aclara una regla, señalando cuál;
  no presentar clasificación como certificación de aceleración.
- [x] Registrar resultados y presentar para aprobación; sin implementación, cierre,
  commit o publicación autorizados por la mera creación de esta tarjeta.

## Investigación inicial — 17 de octubre de 2026

Tarea tomada con autorización del usuario y marcada in-progress. Árbol inicialmente
limpio. Revisados board/search/view, antecedentes 015/027, reglas canónicas y evidencia
Ornith de 035. No hay dependencia bloqueante declarada.

Consulta acotada al commit `2e9f6f487b82200f2a045e67d01a3f1e2982b00a`:
inventario, README y rangos HTTP 206 de los dos GGUF Q8_0. Ambos tienen
`general.architecture=qwen35moe`, `general.type=model`,
`general.name=Ornith Bigbang Mtpfix` y `general.file_type=7` (Q8_0).
El draft tiene 23 tensores y `qwen35moe.nextn_predict_layers=1`; el principal
733 tensores y no declara esa clave. Estas diferencias no establecen una regla
general que distinga un draft separado de un principal con MTP integrado.
El README fijado identifica explícitamente `mtpdraft-Q8_0.gguf` como draft separado
y el archivo `*-embedded.gguf` como principal con cabeza integrada.

Evidencia local: `D:\Coquena\validation\037\inventory.json`, `headers.txt`,
`inspect.py` e `inspect-full.py`. Se consultaron inicialmente 64 KiB por archivo y
luego un rango de 16 MiB por archivo para completar el área de metadatos; los rangos
pueden incluir bytes posteriores a ella. No se instalaron pesos ni se tocó el catálogo.

### Consulta inicial y resolución

La documentación no define qué marcadores de nombre bastan para ofrecer un MTP
como candidato cuando la cabecera no demuestra su rol. Se consulta aceptar
`mtpdraft`/`mtp-draft` y el prefijo/carpeta `mtp` como candidatos explícitos por nombre,
sin elevar su rol a verificado por tener cuantización conocida. Un token `MTP`
interno en el nombre de un principal no bastaría para convertirlo en companion.
La clasificación no certificaría compatibilidad ni aceleración; contradicciones
bloquearían selección. No se ha modificado código ni se han ejecutado pruebas.

El usuario resolvió la consulta en el chat: los marcadores sólo generan candidatos;
`general.type=model`, ausencia de metadatos y otro publicador no son contradicciones.
Se conserva `mtp-` exclusivamente al comienzo del nombre y el token interno MTP no
retira el principal. Se autorizó esta política antes de modificar producción.

## Implementación

- Clasificación compartida por selección, emparejamiento y candidatos de update:
  marcadores delimitados `mtpdraft`/`mtp-draft`, carpeta exacta `mtp` y prefijo
  `mtp-` del basename. Sin excepciones por publicador, tamaño ni número de tensores.
- Un MTP por nombre/carpeta conserva `Candidate` tras inspección; el menú explicita
  origen heurístico y compatibilidad no certificada. Cuantización y rol siguen
  siendo evidencias distintas. `mmproj` conserva su comportamiento.
- Conflictos concretos: marcadores auxiliares incompatibles, MTP frente a `clip`,
  cuantización estándar contradictoria y metadata/split incompatible entre partes.
  La falta de cuantización o un file_type desconocido no ocultan un conflicto previo.
- La documentación funcional incorpora la política de marcadores aprobada y aclara
  evidencia insuficiente frente a incompatible. Es una decisión nueva explícitamente
  acordada, además de aclarar la ausencia de certificación ya documentada.
- Fixture 037 con subconjunto fijado del inventario Ornith y cabeceras mínimas;
  principal Q8_0, draft y principal embedded. Fixtures sintéticos adicionales para
  nombres arbitrarios, mayúsculas, carpetas, límites de token, ausencia/futuro de
  file_type, cuantizaciones Q4_0/Q8_0/BF16, contradicciones y shards.
- Prueba CLI integrada selecciona e instala exclusivamente un GGUF pequeño mediante
  ModelCli con transporte/inspector inyectados; el runner Native AOT ejecuta ese
  mismo camino. El ejecutable publicado se inicia aparte para comprobar los listados
  de principales/companions. No se presenta como descarga interactiva del EXE contra
  Hugging Face, ni como prueba de inferencia.

### Reproducción

Temporales y evidencia en `D:\coquena\validation\037`; esa capitalización evita el
fallo de comparación textual ya documentado en 038. La página de códigos UTF-8 usada
por el procedimiento evita la limitación conocida de 039; no se corrigen esas tarjetas.

```powershell
$env:TEMP = 'D:\coquena\validation\037\temp'
$env:TMP = $env:TEMP
dotnet run --project tests/Coquena.Tests -c Release -- --mtp-tests
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o D:\coquena\validation\037\publish-final -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o D:\coquena\validation\037\tests-final -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
chcp 65001
& specs/035-m2-entrega-integrada/run-native-tests.ps1 -Executable D:\coquena\validation\037\publish-final\coquena.exe -Runner D:\coquena\validation\037\tests-final\Coquena.Tests.exe -Filter '--mtp-tests' -OutputDirectory D:\coquena\validation\037\native-targeted-final -TempDirectory D:\coquena\validation\037\temp
# Suite completa: mismo comando sin -Filter, con otro OutputDirectory.
git diff --check
```

### Hallazgos para revisión

La cabecera general compartida no permite certificar rol separado: se conserva la
heurística visible aprobada, sin convertir conteos de tensores o nextn en garantías.
Esto también limita la futura preparación declarativa M3: seleccionar un recurso no
certifica compatibilidad de ejecución. No se anticipan funciones M3 ni se modifica
el esquema persistido. Los registros existentes no se reclasifican ni migran.

Se preservó un conflicto que antes podía quedar rotulado como no concluyente al
faltar cuantización; un file_type futuro tampoco oculta su diagnóstico incompatible.
La suite completa inicial se detuvo en `external start uses registered executable
and ID`, el caso previamente registrado en 038. No se relajó esa aserción.
### Resultados finales y límites de validación

- Publicación final de CLI y runner Native AOT win-x64 con errores estrictos de
  AOT/trimming: exit 0. Logs `publish-final.txt` y `publish-tests-final.txt`.
- Regresión final focalizada: 217 comprobaciones existentes de 015 y 70 nuevas
  administradas; 217 y 72 en Native AOT con la CLI publicada (incluye dos listados
  iniciados como procesos separados). Exit 0 y stderr vacío. Logs
  `targeted-managed-final.txt` y `native-targeted-final/native-{tests,errors}.txt`.
- La última comprobación añadida verifica que `mtp-model/weights-Q8_0.gguf` conserva
  rol principal: el prefijo `mtp-` sólo aplica al basename, no a cualquier carpeta.
- Suite administrada completa mediante apphost, sin argumento de ejecutable nativo:
  exit 0, log `managed-final.txt`. Corresponde a la revisión anterior a los últimos
  ajustes de diagnóstico desconocido/prefijo de carpeta; esos ajustes se cubrieron
  después con las regresiones focalizadas finales administrada/nativa indicadas.
- La ejecución exploratoria de runner administrado con argumento CLI nativa se
  detuvo en `Installer 55: native extracted executable validated and published`.
  Ese caso extrae únicamente el EXE del runner, y requiere un runner autónomo nativo,
  no un apphost administrado sin sus DLL. No se modificó para ocultar el fallo.
- Validación documental: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales
  de docs; enlaces de esta tarjeta y `git diff --check` válidos. Script/log en
  `D:\coquena\validation\037\validate-docs.ps1` y `docs.txt`.
- SHA-256 de CLI final:
  `9cc5390965eec090559f3688e7a0663f1919bdcc5aace78a70408e5785787e5a`.
- No se descargaron GGUF reales completos ni se inició inferencia. La selección y
  transferencia nuevas usan fixtures pequeños; el HTTP adverso y presupuestos se
  comprueban por las 217 regresiones existentes. Sin paquetes nuevos, cambios a 038/039,
  modificaciones a instalaciones personales, publicación de release ni commit.

Estado: implementación y evidencia aprobadas por el usuario el 17 de octubre de 2026.
Se cierra la tarjeta con LeanSpec y se entrega todo en un único commit autorizado.
Suite completa con runner y CLI Native AOT: **exit 0 y stderr vacío**, logs
`native-full/native-tests.txt` y `native-full/native-errors.txt`. Incluye contratos,
runtime, persistencia, transferencias/contención, backends, resolver, GGUF, instalación,
externos, directorios, update y upgrade. Esta suite usa la revisión con 71 checks 037,
anterior al último ajuste de prefijo exclusivo de basename; la revisión final con ese
ajuste volvió a publicar ambos binarios y pasó 217+72 checks focalizados. No se afirma
una segunda ejecución de la suite completa sobre el último ajuste.

### Verificación independiente del checkout final

El 17 de octubre de 2026 se revisó la implementación y su correspondencia con la
política de marcadores aprobada y la documentación funcional. Sin nuevos bloqueantes.
Se recompilaron producto y runner Native AOT win-x64 con los flags estrictos de AOT
y trimming. La prueba administrada focalizada pasó 217 comprobaciones de 015 y 70
de 037; la focalizada nativa, 217 y 72, con exit 0 y stderr vacío.

Esta pasada sí ejecutó también la suite completa con los binarios Native AOT del
último ajuste de prefijo exclusivo del basename: exit 0 y stderr vacío. Se usaron
temporales en D: con capitalización real y consola UTF-8; no se corrigieron 038/039.
No se descargaron pesos reales ni se inició inferencia. Los archivos de código y
pruebas revisados conservaron sus hashes durante la ejecución.

Evidencia independiente en `D:\coquena\validation\037-review`: `build.txt`,
`managed-targeted.txt`, `publish-product.txt`, `publish-runner.txt`,
`native-targeted/native-tests.txt`, `native-targeted/native-errors.txt`,
`native-full/native-tests.txt`, `native-full/native-errors.txt` y `source-hashes.json`.
Enlaces locales y diff check correctos. La clasificación sigue siendo heurística,
no certificación de compatibilidad o aceleración. Tarjeta abierta para aprobación,
sin cierre, commit ni publicación por esta verificación.


### Aprobación y cierre

El usuario aprobó la entrega y autorizó expresamente cerrar la tarea y realizar un
único commit con código, pruebas, documentación y evidencia el 17 de octubre de 2026.
Tarjeta marcada complete mediante LeanSpec. Sin publicación de release ni push.
