---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 057-m3-verificacion
- 058-m3-novedades
created_at: 2026-10-21T23:26:05.844068300Z
updated_at: 2026-11-02T13:11:19.371670Z
completed_at: 2026-11-02T13:11:19.371670Z
transitions:
- status: in-progress
  at: 2026-11-02T11:10:51.291618500Z
- status: complete
  at: 2026-11-02T13:11:19.371670Z
---
# Aplicar planes selectivos con publicación conjunta recuperable

## Objetivo

Aplicar exactamente un plan vigente conservando activo y resoluciones anteriores ante fallo de cualquier unidad seleccionada.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [descargas.md](../../docs/descargas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Validar vigencia y selector de plan; plan vencido exige nueva consulta, sin resolver otra versión dentro del upgrade.
- [x] Preparar y verificar todas las propuestas válidas seleccionadas antes del commit de resoluciones/inventario, sin éxito parcial de ese conjunto.
- [x] Respetar pin fijo hasta cambio explícito a latest y plan nuevo; preservar variante y protección del build fijado anterior.
- [x] Permitir transferencia con servicio activo conservando revisiones físicas, última buena y referencias de otros consumidores.
- [x] Probar fallos, reanudación, concurrencia y cierre entre fases; informar disponibilidad para siguiente ejecución/aplicación sin reiniciar ni editar intención.

## Dependencias

- [057: Verificar integridad y mantener diagnósticos locales por recurso](../057-m3-verificacion/README.md)
- [058: Consultar novedades y persistir planes exactos por selección](../058-m3-novedades/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin limpieza automática por número de builds, sustitución del activo o recuperación de proceso ajena al contrato de 054.

## Estado y evidencia

Implementación y validación realizadas. Estado `in-progress`, pendiente de revisión
del usuario y autorización de cierre/commit. No se realizó commit, push ni release.

### Decisión acordada

El usuario decidió aplicar las propuestas válidas e informar los recursos bloqueados
sin modificarlos. El conjunto de publicación conjunta está formado por esas propuestas
válidas: un fallo al prepararlas o verificarlas conserva sus resoluciones e inventario
anteriores completos. Los recursos sin cambios y externos se omiten.
Se actualizó la documentación funcional para incorporar expresamente esta decisión;
no era una política definida para conjuntos mixtos en el contrato previo.

### Comportamiento implementado

- `upgrade`, de proyecto o `--global`, consume exclusivamente el plan vigente y
  sus selecciones exactas; acepta los selectores de 058 sin ampliar planes parciales.
  No consulta latest para sustituir su destino. Vigencia por intención, clave y
  resoluciones de partida, conforme al plan persistido, sin caducidad temporal nueva.
- Los bloqueados se reportan con `upgrade-status: blocked` y salida 6, aunque las
  propuestas válidas se apliquen. Un pin fijo se conserva. Cambiarlo a latest
  exige preparar la intención vigente y consultar un plan nuevo; el build antes
  fijado conserva su protección.
- Las transferencias reservan primero GUID, selección e instalación en el conjunto
  local, antes de crear el checkpoint. Los resultados verificados quedan como
  candidatas, fuera del inventario publicado. Una reserva sin checkpoint se
  reconstruye con los mismos identificadores al reintentar.
- Reintentar conserva candidatas y operaciones exactas. `download resume` puede
  completar una candidata sin publicar las resoluciones del conjunto; su salida
  distingue `prepared-for-upgrade` de una instalación publicada.
- Alias con el mismo destino exacto comparten transferencia e instalación.
  Renombres con correspondencia remota demostrada reutilizan los bytes anteriores
  tras verificarlos contra SHA-256 local, sin confundir identidades Git/Xet con
  digest de bytes. Una candidata completa se vuelve a verificar antes del commit.
- Un lease del SO excluye ejecutores del mismo conjunto. La publicación utiliza
  la generación recuperable existente, con comprobación final de intención y
  revisión. Inventario, bindings, locks y resoluciones importadas se publican juntos.
- Se conservan instalaciones anteriores, snapshots de activo/última buena y
  publicaciones de consumidores. No se reinicia, aplica al router, edita YAML ni
  acredita salud del nuevo conjunto. El resultado informa disponibilidad para
  siguiente ejecución/aplicación. No se añadieron paquetes.

### Validación reproducible

Entorno anonimizado, 2026-11-02. Definir `$TestRoot` y `$ProductRoot` como directorios
externos al checkout en el volumen de pruebas acordado. Usar un `$TestRoot` nuevo.

```powershell
dotnet publish src/Coquena -c Release -r win-x64 -o "$ProductRoot"
dotnet run --project tests/Coquena.Tests -- --recipe-upgrade-tests "$TestRoot/upgrade" "$ProductRoot/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --recipe-update-tests "$TestRoot/update" "$ProductRoot/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --scope-transfer-tests "$ProductRoot/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --verification-tests "$ProductRoot/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --help-tests "$ProductRoot/coquena.exe"
git diff --check
```

- Upgrade: 30 comprobaciones correctas con el producto Native AOT. Incluye fallo
  de una segunda unidad independiente, conservación completa del conjunto anterior,
  reintento sin repetir la primera transferencia, selección parcial, recursos
  bloqueados, edición concurrente, exclusión de ejecutores, reserva sin checkpoint,
  reanudación de un prefijo de ocho bytes y publicación posterior desde upgrade.
- Cierres reales de procesos de prueba antes y después de la decisión durable;
  recuperación por lectura y ejecución del producto Native AOT para publicar
  candidatas preparadas, sin red. Corrupción posterior de una candidata es rechazada
  mediante hash completo sin publicar parcialmente.
- Renombre reutilizado sin red; aliases con una sola instalación; pin fijo,
  transición explícita a latest y protección anterior; variante preservada y
  ausencia de consultas nuevas al resolver upgrade del backend; persistencia de
  la selección actualizada en preparaciones posteriores.
- Ámbito global y resolución importada exacta, rechazo de reaplicación de plan
  consumido, y conservación de snapshots activo/última buena simulados.
- Regresiones correctas: novedades 78, transferencias 61, verificación 57, ayuda
  y aliases en administrado/producto Native AOT, incluidos 16 errores sin efectos.
- Publicación Native AOT win-x64 del producto correcta y ejecutable invocado con
  estado y recursos de prueba en el volumen acordado. No se publicaron las pruebas
  como Native AOT ni se ejecutó la suite general o la aceptación integrada de M3.
- No se modificaron diagramas PlantUML ni ejemplos JSON. Las ubicaciones originales
  y los recursos de prueba permanecen fuera de Git.
- Build final correcto, sin advertencias ni errores. Enlaces locales de los Markdown
  modificados: 21 destinos existentes; mapa de código: 228 referencias existentes.
  Revisión de privacidad de evidencia y archivos nuevos, `git diff --check` y
  revisión manual board/search/view correctas. `leanspec deps` no está migrado al
  adaptador; las dependencias 057 y 058 se verificaron mediante sus tarjetas y board.

### Restricciones para revisión

Las operaciones fallidas o invalidadas conservan sus candidatas y selecciones para
diagnóstico y recuperación, conforme a las reglas de persistencia y limpieza.
El estado reside en `local/update-plan.json`, bajo `upgrades`; el registro de cada
conjunto se retira en el mismo commit que publica sus recursos. La limpieza debe
considerar las reservas y candidatas todavía pendientes, además de los checkpoints.
No se incorpora limpieza ni cancelación de conjuntos completos en esta entrega.

El documento local mantiene el límite existente de 16 MiB. Las candidatas añaden
selecciones y manifiestos de archivos, y cada publicación compone una generación.
No se midió su costo con catálogos grandes ni muchas actualizaciones fallidas.
Es una restricción a evaluar para limpieza y las interfaces de M4, sin crear
registros adicionales ni anticipar otra persistencia.

Los snapshots activos de la prueba de actualización son fixtures; no se acredita
una actualización durante inferencia real ni rendimiento de proveedores remotos.
La evidencia acredita transferencias locales, recuperación, invariantes del estado
y ejecución del producto AOT, sin sustituir la aceptación integrada de M3.
