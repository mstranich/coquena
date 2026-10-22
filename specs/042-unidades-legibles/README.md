---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.450198500Z
updated_at: 2026-10-22T16:31:07.423745Z
completed_at: 2026-10-22T16:31:07.423745Z
transitions:
- status: in-progress
  at: 2026-10-22T15:33:46.680845400Z
- status: complete
  at: 2026-10-22T16:31:07.423745Z
---
# Unificar tamaños y memoria con escala 1024 y presentación legible

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo

Aplicar la convención funcional de unidades a la entrada y a todas las salidas humanas existentes, conservando exactitud interna.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [glosario.md#unidades-de-tamaño-y-memoria](../../docs/glosario.md#unidades-de-tamaño-y-memoria)
- [comandos.md](../../docs/comandos.md)
- [observabilidad.md](../../docs/observabilidad.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [x] Centralizar parsing y formato conforme al contrato de unidades del glosario; probar límites y valores inválidos.
- [x] Aceptar los sufijos y alias binarios y las letras decimales del glosario sin distinguir mayúsculas; ajustar ayuda y probar su diferencia.
- [x] Elegir magnitud automáticamente, limitar a dos decimales y quitar ceros innecesarios; comprobar promoción de magnitud tras redondeo.
- [x] Sustituir presentaciones dobles y contadores extensos en bytes de listados, progreso, memoria, espacio, retención y diagnósticos propios de Coquena.
- [x] Conservar cantidades exactas persistidas/estructuradas; no cambiar límites efectivos al cambiar etiquetas ni reinterpretar cifras formateadas por proveedores.
- [x] Probar los puntos de salida existentes y exponer el formateador reutilizable para las entregas de observación y TUI.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Sin rediseñar telemetría, modificar valores numéricos persistidos o reabrir las evidencias históricas de M1/M2. No incorporar otra convención de presentación.

## Estado y evidencia

Implementación terminada el 2026-10-22; tarjeta cerrada tras aprobación explícita. Commit único creado; sin release, tag ni push.

### Implementación

- `ByteSize` centraliza parsing exacto con `BigInteger` y formato con `decimal`, magnitud automática, hasta dos decimales y promoción tras redondeo. Sin nuevas dependencias.
- Parser con sufijos binarios hasta EiB, alias KB–EB en base 1024 y letras k/m/g/t/p/e en base 1000; todos sin distinguir mayúsculas. Punto decimal y rechazo de fracciones de byte/desbordamientos. Un entero sin sufijo expresa bytes. El parser reutilizable admite cero; la política de retención mantiene su requisito positivo.
- Presentación según cultura actual, con proveedor de formato opcional para reutilización en observación/TUI. El resultado redondeado es sólo presentación, no serialización ni entrada para cálculos.
- Aplicado a RAM, artefactos de backend, menú/listado de modelos, selección de actualizaciones, progreso y total de descargas, retención, exceso de logs protegidos y espacio liberado. Tamaño desconocido continúa explícito.
- Ayuda actualizada. No se modificaron contratos persistidos, límites efectivos ni cifras ya formateadas de proveedores. El glosario incorpora la decisión explícita del usuario sobre alias binarios y letras decimales, tomada tras investigar usos de otras herramientas; es una nueva regla de entrada, no una adaptación implícita al código.

### Validación ejecutada

Estado y temporales de prueba en `${TestRoot}`; sin descargas de pesos ni cambios en la home de uso.

- `leanspec board`, `leanspec search unidades`, `leanspec view 042` y revisión manual de tarjeta/milestone: sin dependencias bloqueantes. `leanspec deps` no está migrado al adaptador; se usaron metadatos y tabla del milestone.
- Antes del ajuste de entrada aprobado por el usuario, la batería general completa terminó con salida 0: contratos, configuración, procesos, almacenamiento, backends, descargas/contención, modelos, integridad y actualizaciones. También pasaron las pruebas AOT de descargas (54) y retirada (39).
- Tras el ajuste: `dotnet run --project tests/Coquena.Tests -c Release -- --byte-size-tests`: 85 comprobaciones de unidades/salidas y 58 de retención; salida 0. Incluye todos los sufijos y alias hasta EiB/EB/e, ambas escalas, mayúsculas/minúsculas, decimales exactos, desbordamiento, valores inválidos y preservación del estado. Confirma `1mB` = 1048576 B y `1M` = 1000000 B tanto en parsing como en CLI/persistencia; la salida se normaliza a unidades binarias.
- `dotnet publish src/Coquena -c Release -r win-x64 -o ${TestRoot}/coquena-042/publish`: publicación Native AOT win-x64 del ajuste sin errores ni advertencias. Ejecutor Release con `--byte-size-tests ${TestRoot}/coquena-042/publish/coquena.exe`: 85 comprobaciones de unidades/salidas y 58 de retención con CLI nativa; salida 0. `--help-tests` contra ese binario: ayuda/aliases y 17 errores sin efectos en administrado y Native AOT; salida 0.
- Enlaces locales de glosario y tarjeta, y `git diff --check`: correctos.

### Evaluación para revisión

**Una letra cambia la escala de entrada.** La diferencia entre `1m` y `1MB` es intencional y fue aprobada por el usuario: letras solas usan 1000; sufijos con B usan 1024, incluidos alias sin i. Glosario, ayuda y diagnóstico del parser explican la diferencia. Las pruebas comprueban el valor exacto guardado y la presentación binaria. Puede sorprender a usuarios acostumbrados a otra herramienta; esto es una consideración de usabilidad, no una ambigüedad pendiente ni un hallazgo que genere backlog automáticamente.

El cambio documental incorpora esa decisión nueva de producto. No modifica cantidades ya persistidas ni límites existentes. El formateador público y el parser compartido permiten que observación/TUI de M3 e interfaces de M4 mantengan la misma política; no se anticipan esas interfaces. Sin otros hallazgos relevantes.

La evidencia cubre las salidas actuales y fixtures de transferencia; no acredita inferencia ni interfaces futuras. No se añadieron diagramas ni ejemplos JSON. Trabajo aprobado para cierre y commit único.
