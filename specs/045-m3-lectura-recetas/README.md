---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 044-m3-contextos-persistencia
created_at: 2026-10-21T23:26:04.736157Z
updated_at: 2026-10-22T23:39:39.196900900Z
completed_at: 2026-10-22T23:39:39.196900900Z
transitions:
- status: in-progress
  at: 2026-10-22T21:31:06.973780800Z
- status: complete
  at: 2026-10-22T23:39:39.196900900Z
---
# Leer y componer recetas YAML y variables desde archivos manuales

## Objetivo

Obtener intención efectiva y diagnósticos desde recetas escritas externamente, sin inicialización ni editor obligatorios.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar el subconjunto YAML, límites y vocabulario estructural del formato; rechazar claves duplicadas/desconocidas y formas excluidas.
- [x] Aceptar un único name raíz y como máximo un service singular sin nombre interno; mantener presets, models, companions y arg-defs como secciones independientes.
- [x] Resolver target omitido/default, self y servicio nombrado; validar requisitos sin iniciar ni crear servicios por leer la intención.
- [x] Componer archivos/base/overrides ordenados, mapas, listas, null y backend atómico; conservar procedencia por archivo/campo.
- [x] Interpolar por capa con entorno y archivos env explícitos, respetando prioridad, tipos, valores vacíos, literales y diagnósticos sin exponer secretos.
- [x] Normalizar modelo escalar a su selección larga y exponer consulta efectiva reutilizable sin red ni preparación; integrar sólo sintaxis CLI respaldada por comandos.md, sin inventar un verbo por analogía con otra herramienta.
- [x] Evaluar el parser elegido y acreditar Native AOT antes de adoptarlo; probar rutas externas, límites, composición y errores con fixtures.

## Dependencias

- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin init obligatorio, escritura CLI, editor, resolución remota de pesos ni ejecución de servicios. La validación semántica/emisión de argumentos corresponde a 046.

## Estado y evidencia

Implementación aprobada por el usuario; tarjeta cerrada como `complete`. Código, pruebas, mapa y evidencia se incluyen en un único commit, sin release ni push.

### Implementación

- [RecipeReader](../../src/Coquena/Recipes/RecipeReader.cs) expone `RecipeSelection`, `RecipeReader.Read` y `EffectiveRecipe`: base común, archivos ordenados, intención efectiva, capas/procedencia, defaults consultados sin materializar y resolución explícita de target con el registro existente. Leer no registra identidades, inventario ni combinaciones. La identidad local, si existe, se pasa explícitamente; no se deduce de una lista de archivos.
- [RecipeYaml](../../src/Coquena/Recipes/RecipeYaml.cs) usa eventos del parser, sin deserialización reflectiva; aplica restricciones, tipos y límites antes de aceptar el documento. YAML admite las codificaciones Unicode de [YAML 1.2](https://yaml.org/spec/1.2.2/#52-character-encodings); `.env` exige UTF-8. Archivo mal codificado no se sustituye por texto con pérdida.
- [RecipeStructure](../../src/Coquena/Recipes/RecipeStructure.cs) valida el vocabulario de cada capa antes de combinar y requisitos estructurales efectivos después; normaliza el modelo escalar. Backend y cada definición de argumento se reemplazan completos, conforme a sus documentos responsables; otros mapas combinan por clave y listas reemplazan.
- [RecipeVariables](../../src/Coquena/Recipes/RecipeVariables.cs) aplica env explícitos y entorno del proceso, operadores, anidamiento, literales y conversión estructural. No ejecuta shell, no reinterpreta resultados como YAML ni vuelve a expandirlos.
- [RecipeValue](../../src/Coquena/Recipes/RecipeValue.cs) conserva tipos, comillas, sensibilidad y origen. `Redacted`/`ToString` ocultan toda expansión, incluidos resultados de defaults. `Text` es acceso explícito al valor de runtime: no debe usarse para informes o locks compartibles. Los errores no incluyen valores expandidos, mensajes personalizados de `:?` ni excepciones originales del parser.
- La API conserva `CanConvert` y `ConvertInterpolatedScalar` para la conversión posterior por schema de un argumento enteramente interpolado sin comillas. Args desconocidos permanecen texto; las comillas conservan string. Cargar imports JSON, validar semántica del catálogo, referencias y emitir CLI/INI corresponde a 046: esta consulta no certifica esos argumentos ni su ejecución.
- `comandos.md` define la responsabilidad de consulta efectiva pero no un verbo concreto. No se agregó uno ni se modificó la CLI M2; la consulta reutilizable se acredita ejecutando el núcleo desde el runner nativo. La selección de rutas sigue los contratos de `-f`, env-file y project-directory, sin implementar otros comandos ni inicialización.
- Se amplió [el mapa de código](../../src/code-map.yaml). No se cambiaron reglas funcionales ni diagramas.

### Dependencia y AOT

El usuario autorizó evaluar y adoptar YamlDotNet si superaba Native AOT. Se evaluó la versión 18.1.0, MIT, sin dependencias adicionales para net10.0. Antes de incorporarla al proyecto, una prueba externa con referencia al assembly ya disponible publicó y ejecutó eventos YAML con tipos/comillas en una carpeta temporal externa al repositorio, sin warnings. El producto fija esa versión; no usa el deserializador de objetos.

### Validación reproducible

Desde la raíz del repositorio, definir `$TestRoot` como una carpeta temporal de prueba fuera del repositorio. Las ubicaciones reales de esta ejecución se conservan sólo en el chat; los comandos siguientes parametrizan sus destinos para reproducción:

```powershell
$TestRoot = Join-Path $env:TEMP 'coquena-045'
New-Item -ItemType Directory -Force $TestRoot | Out-Null
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
$env:COQUENA_HOME = Join-Path $TestRoot 'home'
$ProductOutput = Join-Path $TestRoot 'product'
$TestsOutput = Join-Path $TestRoot 'tests'
$ProductExe = Join-Path $ProductOutput 'coquena.exe'
$TestsExe = Join-Path $TestsOutput 'Coquena.Tests.exe'
dotnet run --project tests/Coquena.Tests -c Release -- --recipe-tests
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o $ProductOutput -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -o $TestsOutput
& $TestsExe --recipe-tests
& $TestsExe --project-persistence-tests
& $TestsExe --help-tests $ProductExe
& $ProductExe --help
```

Las pruebas de recetas crean sus fixtures en una carpeta temporal de prueba de D: e imprimen su ubicación; las de persistencia usan TEMP. No se publican las rutas concretas ni los identificadores de ejecución.

- Pruebas administradas específicas: 315 verificaciones OK; fixtures conservados localmente.
- Producto y runner publicados Native AOT win-x64 con warnings como errores: OK. Runner final: 315 verificaciones específicas OK; fixtures conservados localmente.
- Regresión nativa de 044 OK: contextos, identidad física, registro, copia, índice, referencias, corrupción y recuperación entre procesos. Evidencia conservada localmente.
- Ayuda, aliases y 17 errores sin efectos: OK para llamadas al núcleo y al producto Native AOT. El producto publicado ejecutó `--help` correctamente con home de prueba en D:.
- Enlaces locales de la tarjeta y las 80 rutas existentes del mapa comprobados; `git diff --check` OK, también comprobado el formato de los seis archivos nuevos. No se modificaron PlantUML ni ejemplos JSON; no se atribuyen validaciones de esos formatos a esta entrega.
- Revisión manual mediante `board`, `search` y `view`: 045 depende de 044 completa; tras la aprobación explícita se marca `complete`. `leanspec deps` y `leanspec tokens` devolvieron `is not yet migrated to the adapter API`; se verificó la relación en `view` y en la tarjeta, sin presentar esos comandos como exitosos.
- No se ejecutó la suite general completa; la evidencia de regresión de esta entrega es la descrita arriba. No hubo red para adquirir modelos, inferencia ni cambios en el estado real del usuario.

### Límites para revisión

- La superficie de esta entrega es una API, no un comando público de consulta. La publicación del producto no prueba por sí sola que el usuario pueda invocarla desde la CLI; el runner Native AOT ejecuta esa API. Concretar una sintaxis pública exige mantener la gramática canónica coherente, sin importar verbos de Compose por analogía.
- Los límites son por archivo y cantidad de capas, conforme al formato. Se conserva cada capa y el árbol efectivo en memoria. Las pruebas ejercitan individualmente 16 MiB, profundidad 64, 100 000 nodos y 64 capas; no constituyen una medición de memoria o latencia con todos los máximos simultáneos. El consumo agregado es un riesgo a evaluar en combinaciones grandes y en consumidores visuales de M4; no se añadió un límite global distinto al contrato ni se creó backlog.
- No se acredita preparación de recursos, inferencia, edición puntual ni aplicación al router. Las reglas correspondientes pertenecen a sus tarjetas dependientes.
