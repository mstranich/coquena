# Validación integrada M2 — Windows x64

El paquete es una preparación local para revisión. La aceptación, estado y resultados
están en [la tarjeta](README.md). No incluye pesos, llama.cpp ni runtime .NET.
Las reglas de producto están en [CLI](../../docs/comandos.md),
[servidores](../../docs/servidores.md), [modelos](../../docs/modelos-y-companions.md),
[descargas](../../docs/descargas.md) y [parámetros](../../docs/catalogo-de-parametros.md).

## Auditoría de superficie

Se contrastaron las ramas del dispatcher, la ayuda, la documentación canónica,
las tarjetas completas y los casos ejecutados por el runner. Agrupar acciones en una
fila no convierte sus sintaxis en un único comando. Las pruebas nativas usan la CLI
publicada y también los servicios internos; el recorrido integrado complementa esas
pruebas con invocaciones consecutivas de la CLI sobre el mismo home.

| Comandos acordados | Entrega / cobertura ejecutable |
|---|---|
| `system probe` | 021; BackendTests, gramática raíz |
| `backend remote`, `install`, `list`, `use` | 005/006; BackendTests, InstallerTests, regresión M1 |
| `backend register`, selección por nombre externo | 023; ExternalBackendTests, ServerRuntimeTests, recorrido integrado |
| `backend update`, `upgrade` | 024/025; BackendUpdateTests, BackendUpgradeTests; candidato fijado, reutilización, retención y uso activo |
| `backend uninstall` | 034; RemovalTests, recorrido integrado |
| `download list`, `status`, `pause`, `resume`, `cancel` | 004; DownloadTests, InstallerTests, ModelInstallerTests y recuperación de upgrades |
| `model download`, `list` | 007–009/015; resolución, HF, GGUF y ModelInstallerTests |
| `model add`, `companion add`, `--try-match` | 026/029; ExternalModelTests, ExternalMatchingTests, recorrido integrado |
| `model companion download`, `list` | 027; ModelInstallerTests, ExternalModelTests |
| `model [companion] refresh`, `verify`, `--set-baseline` | 028; LocalIntegrityTests; recorrido integrado de principal/companion |
| `model [companion] update`, `upgrade` | 030/031; ModelUpdateTests, ModelUpgradeTests; unidad completa, commit fijado, fallos, cancelación y reservas |
| transición explícita de externo al descargar el mismo nombre | 032; ModelInstallerTests; ID/referencias conservados, originales externos preservados |
| `model remove`, `model companion remove` | 034; RemovalTests, recorrido integrado |
| `param define`, `edit`, `remove`, `support set/remove`, `list/show` y filtros | 018; ParameterTests, ConfigurationTests |
| `config param-reset`, `--dry-run` | 022; ParameterTests; cascada/confirmación/recuperación y dry-run en recorrido integrado |
| `config backend-retention show/set/unset` | 025; BackendUpgradeTests, ConfigurationTests |
| `config log-retention show/set/unset` | 017; RetentionTests |
| `config backends-dir show/set/unset` | 019; BackendDirectoryTests; fusión, rutas relativas, uso activo, recuperación y publicación entre volúmenes |
| `config models-dir show/set/unset` | 020; ModelDirectoryTests; shards, externos, recuperación y publicación entre volúmenes; regresión adicional de servidores |
| `preset create`, `set`, `unset`, `list`, `show`, `remove` | 010/017; ConfigurationTests, ParameterTests, RemovalTests |
| `preset clone`, `model set`, `bind` | 033; ConfigurationTests, ServerRuntimeTests, recorrido integrado |
| `server create`, `default`, `use`, `set`, `unset`, `preset add/remove/list`, `list`, `show` | 011; ServerTests, gramática y referencias; regresión de bases de instalación y externos |
| `server start [-d]`, `restart`, `stop`, `stop --id`, `ps`, `logs [-f]` | 012/013; ServerRuntimeTests, ObservabilityTests, regresión M1 real |
| `server log-retention show/set/unset` | 017; RetentionTests |
| `server [nombre] remove` | Desvío autorizado en 035; ServerRemovalTests, ServerRuntimeTests, recorrido integrado; default, uso activo/incierto, concurrencia y checkpoint recuperable |

Sin funciones adicionales ni comandos ficticios. Import/export JSON de parámetros
y ejecución YAML pertenecen a M3. TUI, HTTP propio, autenticación HF, proveedores
de descarga de terceros y otras plataformas conservan sus exclusiones.

## Reproducción

Ejecutar desde la raíz del checkout con .NET 10 y herramientas Native AOT disponibles.
Fijar los temporales antes de construir o ejecutar pruebas; D: es el destino acordado.
S: es un volumen ReFS virtual alojado en D:, no un segundo disco físico independiente.
Las fixtures de directorios usan dos raíces de volumen distintas (D: y S: con estos
temporales); no prueban fallo físico de discos ni pérdida de la unidad anfitriona.

```powershell
New-Item -ItemType Directory -Path D:\Coquena-task035-validation\temp -Force
$env:TEMP = 'D:\Coquena-task035-validation\temp'
$env:TMP = $env:TEMP
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task035-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task035-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
```

El runner nativo se lanza con una copia del entorno normalizada sin duplicados
Path/PATH, redirigiendo salida y error, y pasando la ruta absoluta del producto.
El helper [run-native-tests.ps1](run-native-tests.ps1) registra ambas salidas y
fija los temporales en D:.
La suite comprueba reset confirmado, selección interactiva, cambios de directorio y
fallos mediante callbacks controlados. No se simula confirmación enviando texto por
stdin redirigido: la CLI rechaza interacciones que no puede obtener.

Tras preparar y extraer el paquete, repetir la suite contra el ejecutable extraído
y ejecutar el recorrido CLI sobre GGUF y backend fixtures:

```powershell
& specs/035-m2-entrega-integrada/run-native-tests.ps1 -Executable artifacts/task035-unpacked/coquena.exe -Runner artifacts/task035-tests-native/Coquena.Tests.exe
& specs/035-m2-entrega-integrada/run-integrated.ps1 -Executable artifacts/task035-unpacked/coquena.exe -Runner artifacts/task035-tests-native/Coquena.Tests.exe
```

El script crea un home nuevo en D:\Coquena-task035-validation y conserva logs. Verifica alta externa,
integridad explícita, companion, clon independiente, dry-run, exclusión de externos
en update/upgrade, inicio, protección de recursos activos, retirada, referencias
rotas y preservación de archivos externos. Esos GGUF mínimos no permiten inferencia.

## Recorrido real autorizado

Reutiliza cuda@13 b11482, Gemma Q4_0 y Bonsai Q2_g64 presentes en D:\Coquena,
registrándolos como externos en un home de prueba separado. No descarga pesos ni
altera catálogos originales. Cada preset usa ctx-size=2048; Gemma emplea 20 capas GPU,
Bonsai todas. El router mantiene un solo modelo cargado y escucha en loopback.

```powershell
& specs/035-m2-entrega-integrada/run-real.ps1 -Executable artifacts/task035-unpacked/coquena.exe -SourceHome D:\Coquena -Port 18085
```

El script exige respuesta final no vacía para ambos presets, consulta métricas/logs,
reinicia y detiene en finally. Tras detener, retira la definición temporal y comprueba
que los logs permanecen. Conserva el home/evidencia. Comprueba que los hashes
de catálogos originales y tamaños/fechas de pesos no cambiaron; no vuelve a verificar
SHA-256 completos de los pesos ni certifica compatibilidad multimodal o calidad.
Las pruebas de pausa/resume, señales de consola y fallos son fixtures de la suite;
no se atribuyen al recorrido real acciones que éste no ejecutó.

## Paquete local

Contenido: `coquena.exe`, `README.md` breve del paquete y licencia.
Los scripts de reproducción necesitan el checkout y el runner; se consultan desde
esta carpeta. El paquete contiene sólo el producto y la guía, sin PDB ni runner.
La guía pertenece a la entrega, no reemplaza la documentación funcional.

```powershell
New-Item -ItemType Directory -Path artifacts/task035-package -Force
Copy-Item artifacts/task035-native/coquena.exe artifacts/task035-package/coquena.exe
Copy-Item specs/035-m2-entrega-integrada/paquete.md artifacts/task035-package/README.md
Copy-Item LICENSE artifacts/task035-package/LICENSE
# README.md: fecha, revisión fuente, SHA-256 del exe y límites de la preparación local.
Compress-Archive -Path artifacts/task035-package/* -DestinationPath artifacts/coquena-m2-win-x64.zip -Force
Expand-Archive artifacts/coquena-m2-win-x64.zip artifacts/task035-unpacked -Force
& artifacts/task035-unpacked/coquena.exe --help
git diff --check
```

El hash del ZIP fija el paquete entregado; timestamps pueden variar al reconstruirlo.
No se publica un release ni se cambia la versión comercial como efecto de esta tarea.
