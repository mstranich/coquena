---
status: complete
created: 2026-11-12
priority: medium
created_at: 2026-11-12T00:22:10.856242700Z
updated_at: 2026-11-12T00:27:42.612703400Z
completed_at: 2026-11-12T00:27:42.612703400Z
transitions:
- status: in-progress
  at: 2026-11-12T00:22:49.905746100Z
- status: complete
  at: 2026-11-12T00:27:42.612703400Z
---
# Incorporar parámetros de caché MoE

## Objetivo y alcance

Incorporar los dos argumentos de caché MoE de llama.cpp al catálogo integrado y
ofrecer definiciones reutilizables para recetas actuales, sin asignar valores,
activar caché ni cambiar instalaciones existentes. Entrega de mantenimiento
durante [M4](../../docs/milestones/M4-experiencia-completa.md).

## Reglas canónicas

- [Parámetros y schema por proyecto](../../docs/design/catalogo-de-parametros.md).
- [Formato y composición de recetas](../../docs/design/formato-proyecto.md).
- [Privacidad de evidencia](../../docs/design/privacidad.md).

## Trabajo y aceptación

- [x] Incorporar tipos, descripción, default informativo y soporte por build en el catálogo de fábrica.
- [x] Conservar los rangos explícitos al construir el catálogo; no atribuir b11482 a todos los argumentos nuevos.
- [x] Ofrecer una capa YAML opcional con rangos numéricos, emisión CLI/INI y builds mínimos oficiales.
- [x] Verificar límites de builds, forks desconocidos, rangos numéricos, emisión y ausencia de asignaciones automáticas.
- [x] Publicar y ejecutar producto y runner Native AOT win-x64.

## Evidencia upstream

- [b11480](https://github.com/ggml-org/llama.cpp/releases/tag/b11480), commit `d6cf9acb25e6c657d6c6a4645ab7b69fce119995`, incorpora `moe-cache-mib`: [PR #29887](https://github.com/ggml-org/llama.cpp/pull/29887).
- [b12191](https://github.com/ggml-org/llama.cpp/releases/tag/b12191), commit `e95426f16982054c379ca7bdc8186996dd22f0fe`, incorpora `moe-cache-cpu-share`: [PR #30966](https://github.com/ggml-org/llama.cpp/pull/30966).
- Las APIs oficiales de releases enlazan esos tags con los commits de incorporación. Se descargaron los binarios oficiales CPU win-x64; `--version` y `--help` confirman el primer argumento en b11480 y ambos en b12191.
- El parser `common/arg.cpp` de b12191 usa int no negativo para MiB y fracción entre 0 y 1 para CPU share. La capa limita MiB a Int32.MaxValue. Esto no acredita capacidad de memoria ni rendimiento.

## Implementación y decisiones

- `ParameterCatalog` añade ambas entradas; `ParameterManager.Factory` conserva soporte explícito en lugar de sustituirlo por la evidencia histórica b11482. La consulta de disponibilidad del catálogo crudo respeta esos límites.
- `docs/examples/llama-moe-cache.yaml` se compone explícitamente con la receta. No se introduce un catálogo global obligatorio: CLI/TUI actuales leen el schema seleccionado por el proyecto.
- La documentación funcional añade un ejemplo de uso y procedencia; no cambia reglas de composición, defaults, forks ni emisión. El mapa enlaza la capa comprobada.

## Validación ejecutada

Entorno de evidencia anonimizado; rutas reales y archivos descargados se conservan
sólo localmente. Definir `$TestRoot` como carpeta temporal externa al repositorio:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --argument-tests
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --parameter-tests
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --argument-tests
& "$TestRoot/tests/Coquena.Tests.exe" --parameter-tests
& "$TestRoot/tests/Coquena.Tests.exe" --configuration-tests
```

- Administrado: argumentos 180 comprobaciones y catálogo 188 comprobaciones OK.
- Producto y runner Native AOT win-x64 publicados sin advertencias ni errores.
- Runner nativo: argumentos 180, catálogo 188 y configuración/presets/INI 119 comprobaciones OK.
- Producto nativo: `--version` y `param list` con receta más capa opcional ejecutados; aparecen ambos argumentos.
- Enlaces locales de los documentos afectados y `git diff --check` comprobados. Sin diagramas PlantUML ni ejemplos JSON nuevos.

## Hallazgos para evaluación y límites

El catálogo integrado y el schema de recetas siguen siendo superficies distintas.
La solución aporta la capa explícita porque los formularios actuales no usan el
catálogo histórico de fábrica como fuente de definiciones. No se modifica esa
política ni se migran catálogos persistidos: actualizar el ejecutable por sí solo
no añade definiciones al YAML ni reemplaza personalizaciones anteriores.

El catálogo histórico expresa tipos pero no límites numéricos; la capa de recetas
sí valida los rangos. La restricción existe en el modelo previo y no se amplió su
contrato para esta entrega. Importa al evaluar una futura unificación de autoría
entre CLI/TUI y otras interfaces, sin autorizarla aquí.

No se realizó inferencia, benchmark, validación de caché efectiva GPU/multi-GPU
ni revisión visual de TUI. No se garantiza aceleración en un equipo concreto.
Commit de esta entrega autorizado por el usuario; sin push ni actualización de la instalación global.
