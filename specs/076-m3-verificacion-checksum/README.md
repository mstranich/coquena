---
status: archived
created: 2026-11-03
priority: medium
tags:
- m3
created_at: 2026-11-03T12:37:35.415541100Z
updated_at: 2026-11-04T19:52:46.489868900Z
completed_at: 2026-11-03T13:23:22.750040100Z
transitions:
- status: in-progress
  at: 2026-11-03T12:37:55.054168700Z
- status: complete
  at: 2026-11-03T13:12:00Z
- status: in-progress
  at: 2026-11-03T13:20:43.342239900Z
- status: complete
  at: 2026-11-03T13:23:22.750040100Z
- status: in-progress
  at: 2026-11-03T13:29:13.372423500Z
- status: complete
  at: 2026-11-03T13:31:34.234751100Z
- status: archived
  at: 2026-11-04T19:52:46.489868900Z
---
# Auditoría rápida y verificación de checksum opcional en verify

## Objetivo

Permitir que la verificación de proyectos y catálogo global audite por defecto a bajo costo (existencia, estructura GGUF, tamaño y legibilidad de archivos) sin recorrer decenas de gigabytes de pesos, reservando el cómputo exhaustivo de hash SHA-256 para la opción explícita `--checksum`.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [comandos.md](../../docs/comandos.md)
- [057: Verificar integridad y mantener diagnósticos locales por recurso](../057-m3-verificacion/README.md)

## Trabajo y aceptación

- [x] Incorporar la opción `--checksum` en la CLI de `verify` tanto en ámbito de proyecto como global.
- [x] Por defecto (sin `--checksum`), verificar receta, existencia, legibilidad y estructura GGUF sin computar SHA-256 byte a byte.
- [x] Con `--checksum`, ejecutar la lectura completa y comparación de SHA-256 contra la referencia registrada.
- [x] Preservar fallos conocidos previos: una verificación rápida sin `--checksum` no limpia un fallo de integridad preexistente si no se ha comprobado el hash con éxito.
- [x] Actualizar la documentación canónica (`docs/actualizacion-y-limpieza.md` y `docs/comandos.md`) y la ayuda de la CLI (`CliHelp`).
- [x] Validar cobertura completa en pruebas administradas y publicación Native AOT.

## Dependencias

- [057: Verificar integridad y mantener diagnósticos locales por recurso](../057-m3-verificacion/README.md)

## Estado y evidencia

Implementada y validada en Windows win-x64; sin commit. Evidencia con ubicaciones anonimizadas, sin mediciones de rendimiento ni inferencia real.

### Decisiones confirmadas

- Por defecto, `verify` realiza una auditoría no destructiva y de bajo costo: valida la estructura y capas de la receta, valida las definiciones de argumentos frente al schema, verifica la existencia física en disco, comprueba apertura y legibilidad (detectando bloqueos de concurrencia exclusivos como `FileShare.None`), valida la concordancia de longitud de archivo cuando está registrada en el inventario y verifica la cabecera/partes de unidades GGUF mediante `GgufInspection`.
- `--checksum` activa la lectura completa byte a byte y el cómputo de digest SHA-256 contra la referencia registrada. Se acepta tanto en el ámbito de proyecto como con `--global` y en `model verify`.
- Preservación de fallos conocidos: la auditoría rápida sin `--checksum` no produce `observed` de digest; si un recurso presenta un fallo conocido previo registrado en `verification-failures`, éste no se elimina hasta que una comprobación con `--checksum` demuestre la restauración íntegra de los bytes esperados.

### Implementación

- La verificación valida primero la emisión de argumentos, luego audita el catálogo y sólo calcula SHA-256 si la auditoría física pasa y se pidió `--checksum`.
- `VerifyCli`: parsea `--checksum` para ámbitos de proyecto y `--global`, actualiza mensaje de ayuda y pasa el indicador a `ResourceVerification`.
- `ScopedModelCli`: admite `--checksum` en la acción `verify` de modelos y companions.
- `ResourceVerification`:
  - `VerifyAsync` incorpora el parámetro booleano `checksum = false`.
  - Cuando `checksum` es falso, comprueba existencia y legibilidad mediante un stream acotado de prueba, valida `size` si está registrado en el inventario, y emite estado `"quick-ok"` (o `"no-reference"` si no posee referencia) sin recorrer el flujo de bytes.
  - Cuando `checksum` es verdadero, invoca `FileIntegrity.VerifyAsync` completo para contrastar el digest SHA-256.
  - Los fallos con referencia se limpian al comparar con éxito el digest original. Un bloqueo previo sin detalle se conserva hasta una comparación SHA-256 satisfactoria de la unidad.
- `verify` muestra un resumen y acciones sugeridas por defecto. `--json` devuelve el informe estructurado. `quick-ok` distingue el éxito de la auditoría rápida de `verified`, que indica coincidencia SHA-256.
- `code-map.yaml`: añade `checksum` a las palabras clave de `recipe-resource-verification`.
- Documentos canónicos: `docs/actualizacion-y-limpieza.md` y `docs/comandos.md` actualizados para expresar la regla del producto.

### Validación ejecutada

- Build de producto y pruebas sin advertencias ni errores.
- Publicación Native AOT `Release`, `win-x64`, y ejecución del binario publicado con fixtures en `D:/tmp/product/coquena.exe`.
- Verificación: **66 comprobaciones** aprobadas en `ResourceVerificationTests`, incluyendo:
  - Verificación rápida por defecto sin cómputo de SHA-256.
  - Detección de incompatibilidad de longitud y archivos inexistentes/bloqueados en modo rápido.
  - Ejecución con `--checksum` para detectar discrepancias de hash.
  - Preservación estricta de fallos conocidos previos ante verificaciones rápidas posteriores.
  - Ejecución del binario Native AOT con y sin `--checksum`.
  - Detección de argumentos no emitibles antes de registrar observaciones y omisión de SHA-256 cuando el tamaño no coincide.
  - Resumen humano por defecto, salida estructurada con `--json`, rechazo de `--checksum` fuera de `model verify` y preservación de bloqueos previos sin detalle por archivo.
- Regresiones verificadas:
  - Combinaciones: **69** comprobaciones.
  - Publicación: **43** comprobaciones.
  - Recuperación de servicios: **24** comprobaciones.
  - Modelos locales por ámbito: **76** comprobaciones.
  - Modelos por ámbito: **41** comprobaciones.
  - Backends por ámbito: **49** comprobaciones.
  - Servicios por ámbito (Native AOT): **39** comprobaciones.
- `git diff --check`: limpio sin advertencias de espacios.

### Consecuencias para revisión y límites

- La verificación rápida verifica presencia, legibilidad, tamaño y coherencia de cabecera GGUF, pero no certifica la inalterabilidad de los tensores internos. Para validar corrupción de sectores de disco o alteración silenciosa de pesos, se debe invocar con `--checksum`.
- No se introducen dependencias nuevas ni cambios en los formatos JSON persistidos en `.coquena/`.

### Reproducción

```powershell
dotnet build tests/Coquena.Tests/Coquena.Tests.csproj --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o "$TestRoot/product"
dotnet run --no-build --project tests/Coquena.Tests -- --verification-tests "$TestRoot/product/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --combination-tests
dotnet run --no-build --project tests/Coquena.Tests -- --publication-tests
dotnet run --no-build --project tests/Coquena.Tests -- --service-recovery-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-local-model-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-model-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-backend-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-service-tests "$TestRoot/product/coquena.exe"
git diff --check
```
