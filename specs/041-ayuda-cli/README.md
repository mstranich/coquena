---
status: archived
created: 2026-10-21
priority: medium
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.363330300Z
updated_at: 2026-10-28T10:21:41.673467Z
completed_at: 2026-10-22T15:08:42.370576800Z
transitions:
- status: in-progress
  at: 2026-10-22T14:07:56.266835700Z
- status: complete
  at: 2026-10-22T15:08:42.370576800Z
- status: archived
  at: 2026-10-28T10:21:41.673467Z
---
# Organizar la ayuda según la sintaxis real del ejecutable

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo

Hacer legible la ayuda actual y permitir identificar exactamente los argumentos y opciones de cada operación.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [x] Contrastar la ayuda con los parsers actuales; separar familias, sintaxis y explicaciones breves sin anticipar comandos todavía no implementados.
- [x] Mostrar argumentos obligatorios, opcionales, repetibles y opciones locales; eliminar cadenas extensas que mezclan operaciones diferentes.
- [x] Precisar dónde se admite --dry-run y rechazar la interpretación de --dry o de una opción de simulación global.
- [x] Explicar los selectores de recurso/operación y el alcance real de -d y -f; comprobar ayuda, aliases existentes y errores sin efectos sobre estado.
- [x] Conservar la semántica de las operaciones: esta entrega sólo modifica su explicación y presentación.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Sin nuevos comandos u opciones, implementación de recetas, cambio de unidades ni instalación global de la herramienta. La ayuda de las operaciones nuevas se entrega en sus tarjetas; la cobertura integrada se revisa en [073](../073-m3-integracion-cli/README.md).

## Estado y evidencia

Implementación terminada y aprobada; tarjeta cerrada.

### Cambios

- Ayuda raíz extraída a `CliHelp`, organizada por familias y una sintaxis por operación.
- Contraste manual con `Cli`, `BackendCli`, `ModelCli`, `DownloadCli`, `ConfigurationCli`, `ParameterCli` y `ServerCli`, incluidos parsers de selectores.
- Se distinguen nombres de recursos, GUID de backend, transferencia e instancia; argumentos opcionales/repetibles, filtros, aliases existentes y opciones locales.
- `--dry-run` aparece sólo junto a las operaciones que lo admiten, con su efecto explicado. Los errores de `--dry` y de simulación global conservan sus pruebas.
- Se conserva la semántica: `-d` sólo en `server start`, `-f` sólo en `server logs`; no se incorporan aliases `service`/`runtime`, YAML ni comandos futuros.
- Sin dependencias nuevas ni cambios de semántica de operaciones. La revisión de documentación funcional solicitada en el chat centraliza y resume las unidades en el glosario; la convención elegida se implementará en 042.

### Evidencia reproducible (2026-10-22)

- `dotnet run --project tests/Coquena.Tests -- --help-tests`: OK.
- `dotnet publish src/Coquena -c Release -r win-x64 -o ${TestRoot}/Coquena-041/publish`: OK, Native AOT win-x64 sin avisos.
- `dotnet run --project tests/Coquena.Tests -- --help-tests ${TestRoot}/Coquena-041/publish/coquena.exe`: OK administrado y nativo. Cuatro accesos equivalentes a ayuda y 17 errores de sintaxis; sin crear home y conservando archivos/directorios de una home existente. Estado aislado en `${TestRoot}/Coquena-041-*`.
- `dotnet run --project tests/Coquena.Tests -- --parameter-tests`: OK, 174 comprobaciones, incluidos dry-run sin cambios, opciones repetibles y aliases de parámetros/soporte.
- `dotnet run --no-build --project tests/Coquena.Tests -- --parameter-tests ${TestRoot}/Coquena-041/publish/coquena.exe`: OK, 174 comprobaciones con CLI nativa.
- Ayuda publicada inspeccionada y conservada en `${TestRoot}/Coquena-041/help.txt`.
- `git diff --check`: OK. Enlaces locales de esta tarjeta comprobados. Sin cambios PlantUML ni ejemplos JSON.
- `leanspec board`, `search 041` y `view 041`: revisión manual de alcance/estado; la tarjeta no declara bloqueos. `leanspec deps 041` no está migrado al adaptador, por lo que las dependencias se comprobaron leyendo la tarjeta y la tabla del milestone.

### Observaciones para revisión

- El ejecutable actual distingue el servidor llamado `default` del predeterminado configurado: `server default show` busca ese nombre; `server show` usa la designación. La ayuda lo explica según el código, sin anticipar la selección por alias de M3. La evolución de servicios corresponde a 053 y la cobertura integrada a 073.
- La entrada de retención actual todavía acepta KB/MB/GB decimal y KiB/MiB/GiB binario. Por decisión explícita del usuario, el contrato funcional y 042 ahora exigen únicamente B/KiB/MiB/GiB/TiB/PiB/EiB. La ayuda de 041 describe el parser existente; 042 debe retirar los sufijos anteriores y actualizar esa ayuda. Esta decisión queda resuelta y no requiere otra evaluación de convención.
- Validación focalizada: sin descargas reales, instalación global ni ejecución de servidores. No se ejecutó toda la suite de runtime porque no se modificaron sus operaciones.

### Revisión de presentación solicitada por el usuario

- Consultadas las ayudas locales de `docker`, `docker-compose`, `fnm`, `winget`, `ffmpeg`, `yt-dlp`, `7z.exe` (en este equipo resuelve NanaZip) y `uv`.
- Índice inicial y sintaxis agrupada con descripciones de acciones; retiradas notas de implementación, identidad interna, planificación y disponibilidad futura. La información necesaria para elegir argumentos permanece junto a cada familia.
- Títulos en negrita y comandos en cian en terminal; salida plana al redirigir, con `NO_COLOR` no vacío o `TERM=dumb`. Sin opciones nuevas ni bibliotecas.
- Líneas de hasta 80 columnas. Pruebas focalizadas actualizadas: cuatro accesos equivalentes, 17 errores sin efectos, ausencia de secuencias ANSI en captura y ancho máximo; OK administrado y Native AOT.
- Repetida publicación Native AOT win-x64 en `${TestRoot}/Coquena-041/publish`; OK. Ejecutado el binario con PTY: colores comprobados quitando temporalmente NO_COLOR y usando TERM=xterm-256color; con el entorno original NO_COLOR=1/TERM=dumb, salida sin estilos.
- `--backend-update-tests ${TestRoot}/Coquena-041/publish/coquena.exe`: OK, 52 comprobaciones. La aserción de ayuda se adapta al argumento renombrado visualmente como `<seguimiento>`.
- Actualizada la captura `${TestRoot}/Coquena-041/help.txt`; `git diff --check`: OK.
