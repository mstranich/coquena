---
status: complete
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.119314200Z
updated_at: 2026-10-11T14:37:37.394921200Z
completed_at: 2026-10-11T14:37:37.394921200Z
transitions:
- status: planned
  at: 2026-10-10T16:05:40.237079200Z
- status: in-progress
  at: 2026-10-11T13:35:24.561987800Z
- status: complete
  at: 2026-10-11T14:37:37.394921200Z
---
# Gestión completa del catálogo de parámetros personalizados

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Permitir crear, consultar, editar y eliminar definiciones mediante param para la
distribución oficial de llama.cpp, incluido el ajuste de builds soportados.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#param)
- [Gestión del catálogo](../../docs/catalogo-de-parametros.md#gestión-de-parámetros-personalizados)

## Alcance
Implementar la gestión local de definiciones, su persistencia y su aplicación al componer
una ejecución, conforme al catálogo funcional. Incluye creación, consulta, edición,
gestión de asociaciones oficiales y eliminación; la importación/exportación tiene una
entrega separada. El origen es siempre ggml-org/llama.cpp (ggml, alias oficial), con
versiones bNNNN y familias oficiales CPU, CUDA y Vulkan. No incorpora orígenes de
terceros ni parámetros específicos de runtimes externos locales.

## Aceptación
- [x] Crear una definición oficial nueva y una excepción para un backend oficial registrado.
- [x] Verificar la separación define/edit: rechazar creación duplicada y edición de
  una definición inexistente sin cambios; conservar campos omitidos durante la edición.
- [x] Consultar y listar sus metadatos y aplicabilidad.
- [x] Editar y eliminar definiciones iniciales y creadas por el usuario; verificar
  la protección de las definiciones host, port y metrics.
- [x] Verificar el diagnóstico de usos y los recorridos de confirmación, cancelación y
  falta de entrada interactiva de la [eliminación](../../docs/catalogo-de-parametros.md#eliminación-de-definiciones).
- [x] Comprobar la cascada en varios presets y servidores, preservando los recursos
  referenciados, y la consistencia del estado ante fallos de persistencia.
- [x] Editar metadatos y asociaciones oficiales sin duplicar la definición ni borrar presets.
- [x] Probar cambios de tipo y enumeración con asignaciones compatibles e incompatibles,
  confirmación, cancelación y fallos conforme a la
  [edición del catálogo](../../docs/catalogo-de-parametros.md#edición-con-asignaciones-incompatibles).
- [x] Validar enumeraciones y defaults contra el tipo declarado.
- [x] Verificar las opciones de tipo, descripción, default, alias, valores enumerados,
  ámbito y destino de define/edit según la [CLI](../../docs/comandos.md#param).
- [x] Verificar opciones repetidas para alias y enumeraciones: reemplazo de la lista
  durante edit, conservación al omitirla y alias que comienzan con guiones.
- [x] Verificar la limpieza explícita de default, descripción y alias; rechazar opciones
  contradictorias y definiciones resultantes con enumeración vacía o default incompatible.
- [x] Verificar que editar el default no modifica asignaciones existentes y que retirar
  una asignación no lo repone, conforme a los
  [defaults para formularios](../../docs/catalogo-de-parametros.md#valores-predeterminados-para-formularios).
- [x] Verificar la [consulta del impacto](../../docs/catalogo-de-parametros.md#consulta-del-impacto)
  en edición y eliminación, sin escrituras ni interacción, incluida la salida a archivo.
- [x] Comparar el informe con la operación real sobre el mismo estado; comprobar que
  un cambio posterior del catálogo o de las asignaciones obliga a recalcular el impacto.
- [x] Verificar enteros, fracciones y booleanos según los
  [tipos documentados](../../docs/catalogo-de-parametros.md#tipos-y-evidencia-del-backend),
  sin inferir el tipo a partir del valor asignado.
- [x] Comprobar asignación al ámbito correcto: parámetros de modelo en presets y
  parámetros de proceso en servidores; guardar definiciones no asigna valores.
- [x] Probar rangos con ambos límites, un solo límite y sin límites; comprobar inclusión
  de los extremos, exclusión de versiones exteriores y versión exacta con límites iguales.
- [x] Verificar la [comparación de builds](../../docs/catalogo-de-parametros.md#comparación-de-versiones):
  orden numérico bNNNN; rechazar rangos invertidos y versiones inválidas.
- [x] Guardar un valor en un preset; incluirlo con backend/versión compatibles y omitirlo
  con warning en una ejecución fuera de su aplicabilidad, conservando el valor guardado.
- [x] Cubrir backends de la misma familia con distinta aplicabilidad.
- [x] Asociar una definición a varias familias/variantes oficiales, con rangos
  independientes; verificar el alias oficial/ggml y la edición de una asociación
  sin alterar las demás ni duplicar la definición.
- [x] Definir asociaciones por familia/variante oficial sin binarios instalados;
  rechazar IDs locales inexistentes al crear o editar una asociación y diagnosticar
  referencias locales ausentes sin reasignarlas por nombre; rechazar orígenes y
  backends no oficiales en consultas y asociaciones.
- [x] Verificar support set/remove y asociaciones generales sin selector, limitadas
  al origen oficial, con rangos evaluados como builds bNNNN.
- [x] Retirar la asociación general sin selector y comprobar que se conservan
  las asociaciones específicas y las asignaciones de valores.
- [x] Verificar la precedencia backend local, origen/familia y general; comprobar que
  una versión fuera del rango específico no recupera el soporte de una asociación general.
- [x] Rechazar argumentos/metadatos inválidos sin modificar el estado.
- [x] Actualizar la ayuda y ejecutar las pruebas de los recorridos anteriores.
- [x] Publicar y ejecutar Native AOT win-x64; registrar evidencia reproducible.

## Límites
No cambia los valores guardados en presets o servidores de forma implícita ni modifica
binarios. La importación/exportación JSON corresponde a [M3](../../docs/milestones/M3-definiciones-declarativas.md), fuera de M2.
Esta entrega guarda el default como metadato; no implementa formularios de terminal o web.
El restablecimiento de fábrica corresponde a [022](../022-m2-config-param-reset/README.md).
El usuario descarga manualmente los runtimes externos y podrá registrar sus binarios
locales mediante [023](../023-m2-backends-externos/README.md). La asociación manual de
parámetros a esos runtimes queda pendiente de una entrega posterior; no bloquea 018.
No se implementan registro de orígenes de terceros, SemVer ni etiquetas arbitrarias.

## Evidencia — 11 de octubre de 2026

Implementados define, edit, remove, support set/remove y consultas persistidas del
catálogo oficial. Se mantienen las definiciones protegidas y la evidencia inicial
acotada a b11482; un rango declarado por el usuario es soporte explícito. La
composición usa las definiciones persistidas y omite parámetros no aplicables con
warning, conservando sus asignaciones. Defaults permanecen como metadatos.

Edición y eliminación comparten el análisis de impacto con dry-run. La confirmación
se solicita fuera del bloqueo de escritura; si cambian catálogo, asignaciones,
selecciones o backends, se recalcula el informe antes de aplicar. Cancelar o carecer
de entrada interactiva conserva el estado. Las referencias retiradas dejan el preset
sin modelo, conservando su ID, el recurso y sus archivos; las instancias activas
mantienen sus archivos de ejecución.

La cascada usa un marcador de transacción publicado atómicamente y snapshots JSON
generados por fuente. Ese marcador es el punto de commit: antes de publicarlo se
conserva el estado anterior; después, los lectores de LocalStore observan todos los
catálogos comprometidos aunque se interrumpa su checkpoint. El siguiente escritor
recupera el checkpoint antes de modificar el estado. Los errores posteriores al
commit informan Published; no se intenta revertir parcialmente ni reejecutar a ciegas.
Dry-run no publica ni recupera checkpoints. La transacción mantiene el límite local
de 16 MiB y rechaza un snapshot mayor antes de modificar los catálogos.

Validaciones ejecutadas:
- Suite administrada completa: exit 0; tras los últimos ajustes, suites dirigidas
  de catálogo (149 comprobaciones), presets/INI (73) y servidores (72): exit 0.
- CLI y runner publicados Native AOT win-x64 con análisis estricto, sin advertencias.
- Suite nativa completa contra la CLI publicada: exit 0. Incluye 149 comprobaciones
  del catálogo, 73 de presets/INI, 49 de retención, 72 de servidores, 66 de procesos
  y 22 de métricas/logs, más contratos, almacenamiento, backends, descargas,
  selección Hugging Face, GGUF e instalación de modelos.
- Cubiertos tipos, defaults, enumeraciones, listas repetidas, limpieza de metadatos,
  protección, rangos inclusivos/abiertos/exactos, precedencia y referencias ausentes.
- Cubiertos informe redirigible sin controles de terminal, confirmación/cancelación,
  falta de interacción, recálculo por cambios concurrentes de catálogo y asignaciones,
  cascadas sobre varios presets/servidores y recursos/archivos preservados.
- Fallos antes del commit, inmediatamente después y entre checkpoints: conservación
  o publicación coherente de los tres catálogos, lectura con una nueva instancia de
  LocalStore y recuperación antes de la siguiente escritura.
- CLI nativa ejecutada en procesos independientes para comprobar persistencia;
  ayuda del ejecutable publicado verificada.
- Validador documental: 37 diagramas PlantUML, tres ejemplos JSON y 183 enlaces locales
  incluidos los de esta tarjeta. git diff --check: exit 0.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --parameter-tests
dotnet restore tests/Coquena.Tests/Coquena.Tests.csproj -r win-x64 -p:PublishAot=true --ignore-failed-sources -p:NuGetAudit=false
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task018-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task018-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task018-tests-native/Coquena.Tests.exe artifacts/task018-native/coquena.exe
& artifacts/task018-native/coquena.exe --help
git diff --check
```

En este host se lanzó la suite nativa mediante ProcessStartInfo con una copia de
variables de entorno normalizada con OrdinalIgnoreCase, para evitar duplicados Path/PATH.
Las pruebas usan homes temporales, modelos GGUF mínimos y backends fixture; no se
modificó ${CoquenaHome}, no se descargaron pesos ni se ejecutó inferencia real.
Sin paquetes nuevos, registro de externos, restablecimiento de fábrica ni import/export.

Los valores de modelo con destino CLI sólo se pueden emitir si coinciden en todos
los presets seleccionados; los casos que no pueden representarse se rechazan antes
del inicio, según la documentación canónica. No se certifica un flag por declararlo:
la definición personalizada sigue siendo responsabilidad de quien aporta el catálogo.
El usuario aprobó la entrega el 11 de octubre de 2026 y autorizó cerrar la tarea y
hacer un único commit con implementación, pruebas, documentación, evidencia y la regla
de separación entre documentación funcional y planificación añadida a AGENTS.md.
Estado actualizado a complete mediante LeanSpec.
