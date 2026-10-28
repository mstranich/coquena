---
status: archived
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
created_at: 2026-10-07T22:02:19.189521800Z
updated_at: 2026-10-28T10:21:41.635396900Z
completed_at: 2026-10-09T11:56:50.687906200Z
transitions:
- status: in-progress
  at: 2026-10-08T23:10:14.232688500Z
- status: complete
  at: 2026-10-09T11:56:50.687906200Z
- status: archived
  at: 2026-10-28T10:21:41.635396900Z
---
# Configuración, presets y generación de INI

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Implementar catálogo mínimo de parámetros sustentado para el backend objetivo y config/preset necesarios al recorrido. Resolver herencia y unset; generar INI Router Mode con dos presets explícitos usando referencias estables y rutas resueltas al componer.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Fixtures de precedencia, unset restaurando herencia, referencias rotas y separación parámetros server/modelo. Verificar nombres/parámetros con evidencia del release elegido, sin convertir desconocido en rechazo ficticio. Sin import/export, clonación ni companions.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación del 8 de octubre de 2026; aprobada por el usuario el 9 de octubre
de 2026. Sin paquetes nuevos ni cambios de las tareas 011–014.

### Entrega

- `config show/set/unset` global, `--family`, `--model` o `--preset`, mutuamente
  excluyentes. Valores propios, herencia y disponibilidad consultiva en las consultas.
- `preset create`, `model set`, `list [--model]`, `show` y `remove`. Identidad GUID
  estable, nombres únicos sin distinguir mayúsculas y referencias rotas visibles.
  Retirar/recrear una entidad con el mismo nombre no repara sus referencias.
- `param list/show` con consultas locales por ID de backend o distribución/release.
  Catálogo mínimo incorporado: 15 parámetros de modelo, la ruta `model` resuelta
  por Coquena y seis parámetros de servidor/router. Host, port y metrics conservan
  sus definiciones estáticas; los comandos de servidor siguen perteneciendo a 011.
- `ConfigurationManager.Compose(backend, presetIds)` ofrece a 011/012 un resultado
  con INI, IDs efectivos de recursos y diagnósticos. No agrega un comando de exportación
  ni inicia servidores. La lista de presets es explícita; valida la unidad GGUF completa,
  resuelve rutas absolutas y emite la primera parte según metadata, sin consultar la red.
- Dos ejes separados y una clave canónica por sección, con orden de claves determinista.
  Los valores desconocidos se conservan con advertencia; no se infieren rechazos ni
  se deduce disponibilidad de una familia de aceleración.

### Persistencia y decisiones técnicas

Se reutilizan los documentos y la exclusión/reemplazo de 003. `config.json` admite
`parameters` y `families` opcionales, de tipo diccionario de literales y diccionario
por variante (`cpu`, `vulkan`, `cuda@13`, etc.). Los documentos anteriores del esquema
1 siguen siendo válidos; se conservan defaults y retención al editar. Modelo y preset
guardan únicamente sus parámetros propios en sus catálogos existentes. ModelId no
se sustituye por nombre o ruta. El contexto JSON generado cubre los nuevos campos.

El render es una función determinista; Compose toma una instantánea bajo el mismo
lease de escritor. Un preset/modelo ausente, shard faltante, inaccesible o inválido
rechaza toda la composición. No omite secciones ni selecciona otra entidad. `unset`
también permite retirar un literal propio antiguo que no pueda representarse en INI.

Para b11482, `common/preset.cpp` trata `;` y `#` como comentarios sin semántica de
comillas y elimina espacios exteriores. Se diagnostican estos caracteres, controles
y espacios exteriores en vez de emitir un valor truncado. Los nombres de preset no
admiten controles, corchetes, comentarios ni `:` (el backend normaliza sufijos tras
dos puntos); `*` y `default` están reservados por su parser. Las rutas afectadas por
esta limitación se diagnostican al componer. Los tipos del catálogo son informativos;
la aceptación semántica de valores y la compatibilidad de inferencia corresponden
al backend. Companions/`--ref`, clonación e import/export permanecen fuera de M1.

### Evidencia de parámetros

Backend instalado inspeccionado: ggml/cuda@13, **b11482**, versión informada
`0.6.0-dev (build 11482, commit b86d2f075)`.

- `llama-server.exe --help` ejecutado contra esa instalación.
- [README del release: Model presets](https://github.com/ggml-org/llama.cpp/blob/b11482/tools/server/README.md#model-presets): secciones, claves, opciones exclusivas de INI y precedencia CLI > preset > global.
- [common/arg.cpp b11482](https://github.com/ggml-org/llama.cpp/blob/b11482/common/arg.cpp): nombres largos, alias y variables de entorno de las definiciones incorporadas.
- [common/arg.h b11482](https://github.com/ggml-org/llama.cpp/blob/b11482/common/arg.h): claves `__PRESET_LOAD_ON_STARTUP` y `__PRESET_STOP_TIMEOUT`.
- [common/preset.cpp b11482](https://github.com/ggml-org/llama.cpp/blob/b11482/common/preset.cpp) y [preset.h](https://github.com/ggml-org/llama.cpp/blob/b11482/common/preset.h): parseo, normalización y sección reservada `default`.

Sólo el origen oficial y ese release reciben evidencia positiva. Otra distribución,
otro release, parámetro ausente o consulta sin selector devuelve `desconocido`.
No se extiende la evidencia de master a releases anteriores o posteriores.

### Verificación reproducible

```powershell
dotnet build Coquena.slnx
dotnet run --project tests/Coquena.Tests -- --configuration-tests
dotnet publish src/Coquena -c Release -r win-x64 -o artifacts/task010-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -o artifacts/task010-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task010-tests-native/Coquena.Tests.exe artifacts/task010-native/coquena.exe
# Ruta de la instalación inspeccionada; ajustar si cambia el home/slot:
& artifacts/task010-tests-native/Coquena.Tests.exe --configuration-backend-smoke ${CoquenaHome}/backends/run/run/llama-server.exe
git diff --check
leanspec view 010
```

El smoke opcional usa un home temporal, puerto loopback libre y dos presets de fixture,
desactiva la carga de pesos y verifica ambos nombres mediante `/models`. Retira sólo
el proceso creado por la prueba y elimina su área temporal. No modifica ${CoquenaHome}
ni acredita inferencia con dos modelos reales; ese recorrido pertenece a 014.

Resultados: build sin errores/advertencias; CLI y ejecutable de pruebas publicados
Native AOT win-x64 con análisis estricto, sin advertencias. Suite completa nativa
exit 0: 9 casos de CLI más 9 del ejecutable, contratos, almacenamiento, 75 comprobaciones
config/presets/INI, 181 de backends, 54 de descargas, 56 de instalación de backend,
217 de resolución GGUF/HF, 168 de selección HF, 127 de GGUF/integridad y 120 de
instalación de modelos. Smoke nativo contra llama-server b11482 exit 0: aceptó los
15 parámetros de modelo y `/models` confirmó `first` y `second`, sin cargar pesos.
`git diff --check` y revisión de enlaces locales correctos. Sin cambios PlantUML.
LeanSpec verificado mediante board/search/view; no se atribuye validación nativa a
los comandos adaptadores. El usuario aprobó el cierre y el commit conjunto de
implementación, pruebas y evidencia el 9 de octubre de 2026.
