# Evidencia de checkpoints y contención

Validación local del 16 de octubre de 2026. Homes sintéticos y temporales bajo
`D:\Coquena\validation\036\tmp`; binarios y logs en
`D:\Coquena\validation\036`. No se usaron ni modificaron los recursos personales
de Ornith. Los fixtures están versionados y no dependen del diagnóstico local anterior.

## Reproducción previa

Antes de modificar el motor, se ejecutó `--contention-tests` con la nueva regresión:

- Control de 2000 ms: parcial/checkpoint 131072/131072, transferencia terminada.
- Retención de 8500 ms: excepción `StorageException`, código `writer-busy`, desde
  el checkpoint de `finally`. La suite falló como se esperaba.

El fixture adquiere `.writer.lock` al empezar el cuerpo HTTP y fuerza el intervalo
del checkpoint periódico, sin pesos remotos ni dependencia de la velocidad del disco.

## Resultado tras la corrección

`ContentionTests` comprueba:

| Caso | Resultado |
|---|---|
| Escritor retenido 2000 ms | Transferencia 131072/131072, sin diagnóstico espurio |
| Escritor retenido 8500 ms | Espera diagnosticada, luego transferencia 131072/131072 |
| Pausa durante contención de 8500 ms | Pausa confirmada con 65536/65536; resume pide Range desde 65536 |
| Cancelación durante contención de 8500 ms | Checkpoint final antes de limpieza; operación eliminada |
| Progreso durante espera | Informa el checkpoint persistido, sin atribuir confirmación a bytes pendientes |
| Caída forzada durante espera | Parcial 65536/checkpoint 0; inconsistencia explícita, resume sin red, cancelación limpia |

La caída usa otro proceso, espera la señal de contención, lo mata y espera su salida
antes de consultar/reanudar. No es una simulación de interrupción por excepción.

`ModelInstallerTests` comprueba además, tanto para modelo como para companion:

- Otra operación completa transferencia/checkpoints/publicación mientras la primera
  conserva su preparación verificada fuera del escritor global.
- Una excepción antes de publicar conserva los archivos transferidos y no activa catálogo.
- Una copia temporal incompleta dejada como tras una caída se reconstruye desde el
  parcial verificado sin red; la publicación termina y limpia el temporal.
- Cancelar elimina ese temporal por GUID sin crear un recurso instalado.
- Las pruebas existentes siguen cubriendo integridad, shards, pausa/reanudación,
  exclusión de ejecutor, colisiones, interrupción entre movimientos/índice y limpieza,
  transición desde externos y preservación de instalaciones publicadas.

## Ejecución reproducible

Desde la raíz del checkout, en PowerShell:

```powershell
$env:TEMP = 'D:\Coquena\validation\036\tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
dotnet build tests/Coquena.Tests -c Release
& tests/Coquena.Tests/bin/Release/net10.0/Coquena.Tests.exe --contention-tests
& tests/Coquena.Tests/bin/Release/net10.0/Coquena.Tests.exe --download-tests
& tests/Coquena.Tests/bin/Release/net10.0/Coquena.Tests.exe --model-install-tests
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o D:\Coquena\validation\036\native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o D:\Coquena\validation\036\native-tests-final -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& D:\Coquena\validation\036\native\coquena.exe --help
& D:\Coquena\validation\036\native-tests-final\Coquena.Tests.exe --contention-tests
```

Para las suites con CLI nativa, pasar el ejecutable del producto como segundo argumento:

```powershell
& D:\Coquena\validation\036\native-tests-final\Coquena.Tests.exe --model-install-tests D:\Coquena\validation\036\native\coquena.exe
```

Resultados registrados:

- Build Release: cero errores y advertencias.
- Suite administrada de descargas: 53 comprobaciones.
- Suites administradas específicas: modelos 241 en la primera pasada; upgrade de
  modelos 100, upgrade de backends 45, directorio de modelos 64, directorio de backends 96.
- Suite nativa con ejecutable del producto: descargas 54, modelos 252 (incluye los
  casos añadidos de cancelación de temporales), upgrade de modelos 101, upgrade de
  backends 51, directorio de modelos 66, directorio de backends 103; todas correctas.
- Regresión final, administrada y Native AOT: cuatro casos de contención y caída
  forzada correctos. El runner final incluye la prueba de caída en otro proceso.
- Publicación Native AOT win-x64 del producto y runner, con avisos de AOT tratados
  como errores; ejecución real de `--help` y comandos de las suites específicas.
- La suite completa administrada no terminó: tras contratos, retirada, configuración,
  parámetros, retención y servidores, falló en `external start uses registered executable
  and ID` de `ServerRuntimeTests`. No se modificó esa aserción ni se atribuyó su causa
  a esta corrección. Las suites afectadas se ejecutaron por separado.
- `leanspec deps` devuelve `not yet migrated to the adapter API`; dependencias revisadas
  manualmente con board/view y la tarjeta, sin bloqueos pendientes declarados.

Logs locales: `contention-final.txt`, `contention-native-final.txt`, `suite.txt`, `publish-product.txt`,
`publish-tests-final.txt`, `native--download-tests.txt`, `native--model-install-tests.txt`,
`native--model-upgrade-tests.txt`, `native--backend-upgrade-tests.txt`,
`native--model-directory-tests.txt`, `native--backend-directory-tests.txt`.

## Límites y consecuencias para revisión

La espera del checkpoint continúa mientras el escritor siga ocupado; pausa y cancelación
no prometen devolver el prompt en un tiempo fijo. Es la aplicación de la regla de persistir
antes de confirmar el control. Errores distintos de contención siguen siendo explícitos.

La transferencia y su JSON no forman una transacción atómica frente a un cierre forzado.
No se recuperan parciales inconsistentes antiguos ni nuevos mediante adopción o truncado.
La prueba de caída verifica el rechazo seguro, no la recuperación de bytes sin confirmar.

La preparación de adquisiciones y su reversión ya no copia ni calcula hashes dentro del
escritor global. Upgrade y traslados mantienen su coordinación anterior; sus fases pesadas
pueden demorar otros comandos, aunque el checkpoint vivo ahora conserva el ejecutor y
espera la persistencia. Para la preparación de recursos de M3, evaluar latencia y visibilidad
de espera si se exige mayor concurrencia; esta corrección no introduce scheduler ni daemon.

No cambió una regla funcional ni se editaron documentos canónicos para acomodar el código.
La tarjeta permanece `in-progress`, presentada para revisión, sin commit ni publicación
de una release. Publicar binarios locales de validación no constituye publicar una release.

## Corrección del hallazgo de integridad durante revisión

El revisor reprodujo una sustitución del temporal entre `AfterPreparation` y el
movimiento por ruta. La protección anterior era insuficiente: `FileShare.Delete`
permitía renombrar el archivo verificado y colocar otro en su ruta.

Se incorporó `PublicationIdentityTests` antes de cambiar el producto. Tras restaurar
el `CrashWorker` ausente en el checkout de revisión, la compilación funcionó y la
nueva prueba falló en «substituted temporary rejected by identity»: el producto
aceptaba la sustitución. Log `D:\Coquena\validation\036-review\identity-before.txt`.

### Corrección y fundamento

La preparación mantiene copias y hashes fuera del escritor global. Al hacer commit:

1. Se comprueba que la ruta temporal identifica el mismo archivo que el handle
   verificado, mediante volumen e ID de 128 bits.
2. Se mueve el temporal sin sobrescribir destinos.
3. Se abre el destino con `FileShare.Read`, sin compartir escritura ni delete, y se
   vuelve a comparar su identidad con el handle verificado. Esta segunda comprobación
   cierra la ventana entre la primera consulta y el movimiento.
4. Se conserva ese guard hasta completar la publicación del índice y liberar la
   exclusión global. No se calcula otro hash dentro del escritor global.

Para `existing=true`, el propio handle usado para calcular el hash se abre sin
compartir delete; la protección permanece aunque el checkpoint de movimiento ya
esté avanzado y no se vuelva a invocar `Commit()` para ese archivo.

La comparación sigue el contrato de
[FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info):
volumen e identificador completo, evitando limitarse a nombres, fechas, tamaños o
un identificador de 64 bits. El mecanismo es Windows, acorde con el destino de la
entrega. Si no se puede consultar identidad, falla con `file-identity-unavailable`;
no acepta identidad desconocida ni vuelve a hashear pesos bajo exclusión.

No cambian las reglas funcionales: el conjunto validado es el único que puede
activarse. Se corrige el mecanismo que incumplía esa regla. No se agregan paquetes,
recuperación por adopción ni una migración de datos.

### Regresiones nuevas

Cada escenario se ejecuta para modelo y companion:

- Sustituir el temporal con un byte alterado: `publication-file-changed`, sin mover
  el reemplazo al destino ni crear registro; resume reconstruye desde el parcial
  verificado sin red.
- Sustituir el temporal con bytes idénticos: también se rechaza, demostrando que se
  comprueba identidad y no sólo contenido/tamaño.
- Sustituir el destino justo después del rename y antes del guard: el segundo control
  detecta la identidad distinta, no activa catálogo y resume rechaza la integridad
  alterada sin red. Se conserva el archivo para diagnóstico; no se borra por adivinación.
- Intentar renombrar un destino ya existente después de su verificación: Windows
  rechaza la operación por sharing violation; se instala el contenido verificado.
- Intentar renombrar el destino en `AfterMove`, con el guard ya adquirido: Windows
  rechaza la sustitución hasta completar el índice; hash instalado y referencia coinciden.

La restauración de `CrashWorker` incluye su invocación desde `Run`, los controles
de diagnóstico de contención y la reanudación por Range desde 65536. No se resolvió
el error de compilación retirando la prueba de caída.

### Reproducción y resultados de la revisión

Raíz de esta verificación: `D:\Coquena\validation\036-review`; TEMP y TMP bajo su
carpeta `tmp`. No se modificaron los recursos personales ni el código del diagnóstico.

```powershell
$env:TEMP = 'D:\Coquena\validation\036-review\tmp'
$env:TMP = $env:TEMP
dotnet build tests/Coquena.Tests -c Release
& tests/Coquena.Tests/bin/Release/net10.0/Coquena.Tests.exe --model-install-tests
dotnet build src/Coquena/Coquena.csproj -c Release -r win-x64
dotnet run --project D:\Coquena\validation\036-review\publication-probe\probe.csproj -c Release
```

La DLL `win-x64` referenciada por el diagnóstico se recompiló antes de repetirlo.
Ahora el diagnóstico muestra `Run result: failed`; el catálogo queda vacío. La
llamada posterior a `.Single()` del diagnóstico original lanza `Sequence contains
no elements` porque suponía que la instalación había tenido éxito. Se conserva su
salida en `publication-probe-after.txt`; esa excepción no es del producto.

Suite administrada de modelos: **305 comprobaciones**, correcta. Descargas: **53**;
upgrade de modelos **100**, upgrade de backends **45**, directorio de modelos **64**
y directorio de backends **96**, correctas. Publicación Native AOT del producto y
runner win-x64 con los mismos flags estrictos anteriores, sin errores ni advertencias.

La ejecución final con ambos binarios Native AOT pasó **689 comprobaciones**:
modelos/companions **314**, descargas **54**, upgrade de modelos **101**, upgrade de
backends **51**, directorio de modelos **66** y directorio de backends **103**.
Además pasaron los cinco escenarios de `--contention-tests`, incluida caída forzada,
diagnóstico durante espera y resume por Range. La CLI nativa ejecutó `--help` y los
comandos de las suites; enlaces locales y `git diff --check` correctos.

Los logs finales están en `D:\Coquena\validation\036-review`:
`identity-before.txt`, `identity-after.txt`, `publication-probe-after.txt`,
`publish-product.txt`, `publish-tests.txt`, `native-help.txt` y
`native--model-install-tests.txt`, `native--download-tests.txt`,
`native--model-upgrade-tests.txt`, `native--backend-upgrade-tests.txt`,
`native--model-directory-tests.txt`, `native--backend-directory-tests.txt`,
`native--contention-tests.txt`. Para repetir las suites nativas, usar los binarios
`native/coquena.exe` y `native-tests/Coquena.Tests.exe` de esa raíz con los filtros
anteriores. Estos resultados sustituyen la validación de la versión vulnerable.

La suite completa conserva la limitación de runtime documentada arriba; no se
atribuye una causa nueva ni se corrigen las tareas 038/039 en esta revisión.
La tarjeta sigue abierta, sin commit ni publicación de release.

## Verificación independiente posterior

Se revisó nuevamente el checkout corregido, sin modificar producción ni pruebas.
La compilación Release pasó con cero errores/advertencias y la suite administrada
de modelos completó 305 comprobaciones. Se recompilaron producto y runner Native AOT
win-x64 con avisos de AOT tratados como errores en `D:\Coquena\validation\036-review`.

El diagnóstico original se repitió después de recompilar la DLL que referencia:
la sustitución del temporal devuelve `Run result: failed` y no registra el modelo.
La excepción posterior de `.Single()` pertenece al diagnóstico, que esperaba una
instalación; no es un fallo adicional del producto.

Las suites nativas se ejecutaron de nuevo contra los binarios recién publicados:
modelos 314, descargas 54, upgrade de modelos 101, upgrade de backends 51,
directorio de modelos 66 y directorio de backends 103: total 689 comprobaciones.
Los cinco escenarios de contención/control/caída también pasaron, y el producto
nativo ejecutó `--help` correctamente. Se verificaron enlaces locales y diff check.

Logs de esta pasada: `build-final.txt`, `models-managed-final.txt`,
`publish-product-final.txt`, `publish-tests-final.txt`, `probe-independent-final.txt`,
`contention-independent-final.txt`, `contention-independent-errors.txt` y las carpetas
`independent--*`, cada una con `native-tests.txt` y `native-errors.txt`.
Los hashes de los archivos de producción revisados y de ContentionTests permanecieron
sin cambios durante la verificación; snapshot en `review-start-hashes.json`.

No se encontraron nuevos bloqueantes en este alcance. No se volvió a ejecutar la
suite completa de runtime ni se corrigieron las limitaciones de 038/039. La espera
sin plazo fijo y el rechazo explícito de identidad no consultable permanecen como
límites documentados. Esta verificación no cierra la tarjeta ni autoriza commit o release.

## Aprobación y cierre

El 17 de octubre de 2026 el usuario aprobó expresamente la entrega y solicitó cerrar
la tarea y hacer un único commit con todo lo aprobado. Se conserva la validación
anterior: 305 comprobaciones administradas, 689 nativas y cinco escenarios de
contención/caída, con los límites de la suite completa indicados arriba.
Los hashes de producción y ContentionTests coinciden con la versión revisada;
el cierre sólo actualiza documentación y metadatos de planificación.

Se cierra la tarjeta mediante LeanSpec y se integran código, pruebas y evidencia
en el mismo commit. Las tareas 037–039 conservan su estado y alcance. Este cierre
no actualiza el binario publicado de 0.2.0 ni publica otro tag o release.
