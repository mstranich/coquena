---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
created_at: 2026-10-21T23:26:04.830589Z
updated_at: 2026-10-23T13:31:20.761943900Z
completed_at: 2026-10-23T13:31:20.761943900Z
transitions:
- status: in-progress
  at: 2026-10-23T02:52:27.046476600Z
- status: complete
  at: 2026-10-23T13:31:20.761943900Z
---
# Validar argumentos y emitir CLI e INI con schema optativo

## Objetivo

Entregar validación y emisión segura de argumentos según el schema opcional del ámbito seleccionado.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [servidores.md](../../docs/servidores.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar arg-defs imports/definitions, metaschema y reglas cerradas de tipos, ámbitos, aliases, restricciones y relaciones declarativas sin ejecutar código aportado.
- [x] Respetar args-mode permisivo por defecto y estricto global/local; validar argumentos conocidos en ambos modos y diagnosticar desconocidos en estricto.
- [x] Emitir booleanos, literales, listas, repeticiones, separadores, null y referencias resueltas sin inferir aridad o escapes incompatibles con el destino.
- [x] Pasar tokens CLI sin shell y usar codec INI acreditado por adaptador/build; rechazar valores sin representación reversible y composiciones imposibles por preset.
- [x] Entregar operaciones de dominio de param y consulta de impacto reutilizables; la persistencia puntual de comandos en YAML se integra en 071.
- [x] Probar schema importado/inline y precedencia, aliases conflictivos, listas, tipado de interpolación, reglas inválidas y ausencia de defaults materializados automáticamente.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No depende de editar YAML desde CLI. Sin formularios, generación IA de schemas ni atribuir APIs de control al catálogo de argumentos.

## Estado y evidencia

Implementación aprobada para cierre; la tarjeta se completa con la evidencia indicada y un único commit de la entrega.

### Decisiones aprobadas

- Interpolación string por defecto; conversión y validación explícitas mediante `|string`, `|integer`, `|number` o `|boolean`, sin selección de tipo por any-of ni por el campo.
- Evaluar reglas también para argumentos propietarios ausentes; `when: {present: false}` puede exigir una alternativa. False, cero y string vacío cuentan como presentes; null retira la asignación.
- Integer exige notación entera sin punto decimal ni exponente; number admite notación entera, decimal y científica con valor finito. La regla se aplica al schema y a los sufijos explícitos de interpolación.
- La referencia a edición puntual se corrige a 071 conforme al milestone vigente, sin ampliar esta entrega a persistencia YAML.

### Implementación y evidencia de revisión

- `ArgumentSchema` carga imports JSON por capa y definiciones inline, aplica reemplazos completos y null, comprueba el vocabulario cerrado, aliases, tipos, restricciones, defaults, aplicabilidad, emisión y relaciones. No ejecuta código del catálogo, no carga `$ref` ni materializa defaults.
- `ArgumentValidation` valida los argumentos efectivos en ambos modos y los ámbitos server/preset; canonicaliza aliases, comprueba identidad de referencias antes de emitir y evalúa reglas también para propietarios ausentes. La aplicabilidad desconocida se informa sin convertirla en incompatibilidad; un incumplimiento comprobado se rechaza.
- `Define`, `Remove` e `Impact` trabajan sobre candidatos inmutables. Impact devuelve el primer error de cada bloque afectado, con ubicación; no modifica ni descarta las asignaciones anteriores. Las interfaces pueden corregirlo y volver a consultar para revelar otros errores del mismo bloque.
- `ArgumentEmission` genera tokens separados y `ProcessStartInfo.ArgumentList` sin shell; exige reglas de listas, evidencia de acumulación para repeat y codec INI explícito. Rechaza colisiones, separadores ambiguos, referencias sin resolver y asignaciones por preset que el proceso no puede representar independientemente.
- El adaptador incluido reutiliza exclusivamente la evidencia histórica b11482 de [010](../010-m1-config-presets-ini/README.md). No se acredita otro build, recarga, inferencia ni APIs de control; la matriz ejecutada del build elegido corresponde a 052. Los callbacks de referencia reciben identidad tipada y devuelven una sola ruta de entrada; esta entrega no adquiere ni prepara recursos.
- Se actualizó el contrato funcional de interpolación por decisión explícita del usuario y se aclaró la evaluación de reglas ausentes. La emisión declarada exige mode; name omitido conserva la clave canónica. No se modificaron comandos M2 ni se agregó persistencia YAML.

### Validación ejecutada

Entorno de prueba anonimizado; las ubicaciones originales y salidas completas quedan sólo en evidencia local. Estos resultados verifican comportamiento y no acreditan rendimiento equivalente en otra máquina. Definir `$TestRoot` como carpeta temporal externa al repositorio en el volumen de pruebas acordado:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
$env:COQUENA_HOME = Join-Path $TestRoot 'home'
$ProductOutput = Join-Path $TestRoot 'product'
$TestsOutput = Join-Path $TestRoot 'tests'
$ProductExe = Join-Path $ProductOutput 'coquena.exe'
$TestsExe = Join-Path $TestsOutput 'Coquena.Tests.exe'
dotnet run --project tests/Coquena.Tests -c Release -- --argument-tests
dotnet run --project tests/Coquena.Tests -c Release -- --recipe-tests
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o $ProductOutput -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -o $TestsOutput
& $TestsExe --argument-tests
& $TestsExe --recipe-tests
& $TestsExe --project-persistence-tests
& $TestsExe --help-tests $ProductExe
& $ProductExe --help
```

- Build administrado: sin advertencias ni errores; 160 verificaciones de argumentos y 314 verificaciones de recetas OK.
- Producto y runner Native AOT win-x64: publicados sin advertencias ni errores; las mismas 160 y 314 verificaciones OK. Tras resolver la política numérica se amplió y republicó el runner: 160 verificaciones OK; el producto, cuyo núcleo no cambió, volvió a ejecutar `--help`. Se ejecutó un proceso hijo real para contrastar tokens con espacios y caracteres de shell; no se usó shell para ejecutarlos.
- Regresión nativa de contextos, identidad física, registro, copia, índice, referencias, corrupción y recuperación entre procesos: OK. Ayuda/aliases y 17 errores sin efectos: OK. El producto publicado ejecutó `--help`.
- Pruebas de regex: rechazo de lookaround/backreferences, conservación de grupos atómicos permitidos, literales dentro de clases y diagnóstico no concluyente al agotar el plazo de 100 ms.
- Enlaces locales de los documentos afectados y rutas existentes del mapa comprobados; `git diff --check` OK. No se modificaron diagramas PlantUML. Los JSON de imports válidos e inválidos se ejercitaron como fixtures, sin agregar archivos JSON de producto.
- No se ejecutó la suite general completa ni inferencia real. La funcionalidad de dominio se ejecutó desde el runner Native AOT; el producto publicado no incorpora todavía una superficie CLI para estas operaciones.

### Resolución de la política numérica

El usuario confirmó que integer exige notación entera y que las formas `8192.0` y `8.192e3` se admiten sólo para number. El núcleo existente ya sigue esta política; se actualizó el contrato funcional y se añadió una matriz de regresión para ambos modos de validación y los sufijos de interpolación.
