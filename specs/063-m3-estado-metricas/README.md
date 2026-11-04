---
status: archived
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 042-unidades-legibles
- 053-m3-servicios
created_at: 2026-10-21T23:26:06.261807300Z
updated_at: 2026-11-04T19:52:46.478514900Z
completed_at: 2026-11-02T20:49:43.925382800Z
transitions:
- status: in-progress
  at: 2026-11-02T20:09:01.343380400Z
- status: complete
  at: 2026-11-02T20:49:43.925382800Z
- status: archived
  at: 2026-11-04T19:52:46.478514900Z
---
# Consultar servicios y hardware con métricas atribuibles y salida finita

## Objetivo

Entregar ps y snapshots reutilizables con fuente, unidad y disponibilidad sin efectos sobre el backend.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Consultar todos los servicios conocidos o el seleccionado, globales y propios, verificando identidad/proceso/API/salud y aislando fallos.
- [x] Mostrar composición realmente activa frente a intención actual, incluido fallback y estado local de nombres; no inferirla del YAML editado.
- [x] Obtener CPU/RAM y GPU/VRAM con alcance comprobado por proceso/dispositivo/global; no atribuir totales compartidos a cada servicio ni inventar cero.
- [x] Consultar métricas/eventos disponibles sin cargas, recargas o reinicios por observar; mostrar memoria y tamaños con el formato único de 042.
- [x] Probar proceso vivo sin API, PID reutilizado, backend sin telemetría y recursos compartidos; exponer snapshots de dominio para CLI/TUI.
- [x] Exponer muestras fechadas y su vigencia, con acceso acotado por instancia y fallo aislado; la reconstrucción de fases/tareas se implementa en 065.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [053: Registrar servicios y operar su proceso con identidad segura](../053-m3-servicios/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Ps sin opciones termina tras la instantánea. Sin benchmark, control de modelos, supervisor o componentes visuales.

## Alcance acordado de plataformas

- Implementar la abstracción de métricas del sistema operativo con proveedores
  Windows y Linux, sin ejecutables externos de fabricantes ni nuevos paquetes.
- Windows: API de CPU/memoria, PDH y DXGI. Linux: procfs, interfaces DRM del kernel
  y NVML provista por el driver cuando esté disponible. Ausencias y errores se
  conservan como no disponibles; no se exige soporte idéntico entre fabricantes.
- Validar el proveedor Linux en WSL2 e identificar sus límites; no presentar esa
  evidencia como validación en Linux nativo. Esto no amplía el ciclo de servicios
  persistidos de 053 a Linux: su identidad de proceso se verifica sólo en Windows.
- La abstracción de observación de instancias separa verificación, escucha y
  medición del sistema operativo de la composición y consulta de métricas HTTP.

## Estado y evidencia

Implementación realizada y validada. Sin push, tag o release. Evidencia con entorno
anonimizado: no se publican el perfil
de hardware, identificadores de dispositivos, rutas originales ni logs crudos.

### Implementación

- `ps` general, global o por servicio usa registros de ámbito e índice de proyectos;
  incluye detenidos y el alias default. No inicia, reconcilia procesos ni aplica
  retención de logs. Reutiliza el selector de servicios existente.
- `ISystemMetrics` tiene proveedores Windows/Linux; `IInstanceProbe` conserva
  verificación de identidad, escucha y medición de procesos fuera de la consulta
  HTTP. Los snapshots de hardware, servicios, entradas y muestras son consumibles
  por otras interfaces, con fecha, fuente, unidad, alcance y motivo de ausencia.
- CPU de instancia: diferencias del accounting del job verificado, normalizadas
  por CPU lógica. Memoria privada comprometida: suma de miembros verificados;
  conjuntos residentes: muestras individuales por PID/creación, sin sumarlos.
  GPU/VRAM globales se muestran por dispositivo; no se atribuyen a cada servicio.
- La intención se lee aparte del snapshot activo. Se conservan composición
  registrada, resultado de recuperación, diferencias de nombre local y entradas
  realmente observadas. Una discrepancia o cambio durante la captura impide
  confirmar la composición como actual; la consulta no promueve última buena.
- HTTP usa un presupuesto compartido de cinco segundos por instancia, hasta
  1 MiB por respuesta y hasta 128 entradas por captura. Consulta salud, `/models`
  y `/metrics?model=...&autoload=false` sólo para entradas cargadas; no solicita
  reload, load, warmup ni conversaciones. El catálogo puntual aporta los estados;
  suscripciones SSE/reconexión corresponden a 064, y fases/tareas a 065.
- Se reutiliza el parser de métricas acreditado en 052. Las tasas conservan su
  carácter medio y los contadores su carácter acumulado. No se calculan tasas
  totales ni diferencias entre generaciones de procesos de modelos desconocidas.
- La documentación funcional incorpora la decisión autorizada de usar API/drivers
  sin utilidades externas; aclara memoria comprometida/residente, muestras no
  simultáneas, virtualización y selección de ps. No modifica la vida del backend.
- Dos correcciones puntuales en pruebas existentes permiten publicar el runner
  AOT con advertencias como errores: sobrecarga de `JsonArray.Add` sin serialización
  dinámica y ubicación del assembly administrado mediante `AppContext.BaseDirectory`.

### Validación

- Build Release administrado sin advertencias; suite de observación Windows:
  37 comprobaciones, incluidos procesos reales, consulta del producto publicado
  y router real sin cargar modelos. No se descargaron pesos.
- Native AOT win-x64 de producto y runner, con advertencias como errores;
  suite de observación: 37 comprobaciones. Ayuda/aliases: 16 errores de sintaxis
  sin efectos, en rutas administrada y nativa. Seguimiento de logs: 47 comprobaciones.
- Native AOT linux-x64 de producto y runner en WSL2, SDK .NET 10.0.401 local y
  linker GCC. Suite de hardware: 9 comprobaciones. Ejecución de `ps` publicado:
  CPU/RAM por procfs y GPU/VRAM por NVML disponibles en este entorno; los valores
  e identificadores originales se conservan sólo en evidencia local.
- DRM se comprueba con fixtures de contadores presentes, inválidos y ausentes.
  WSL2 no acredita hardware AMD/Intel real, Linux nativo ni el total de CPU/RAM
  del host. No se implementa identidad/ciclo de servicios Linux.
- Límites HTTP, métricas desactivadas/ausentes, PID reutilizado, nombres editados,
  fallback registrado, servicio detenido, selección general/global/default y
  memoria/dispositivos compartidos quedan cubiertos por las pruebas focalizadas.
- Enlaces Markdown locales, rutas del mapa de código, whitespace de fuentes nuevas
  y `git diff --check`: correctos. Sin cambios en PlantUML ni ejemplos JSON
  documentales. Revisión manual mediante board/search/view; `leanspec deps` y
  `leanspec tokens` siguen sin migrar al adaptador, por lo que no acreditan checks.

Reproducción Windows (definir rutas propias; backend local compatible ya disponible):

```powershell
$TestRoot = Join-Path $env:TEMP 'coquena-observation-validation'
$Backend = $env:COQUENA_TEST_BACKEND
dotnet publish src/Coquena -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --observation-tests "$TestRoot/product/coquena.exe" $Backend
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --service-log-follow-tests "$TestRoot/product/coquena.exe"
```

El tercer argumento opcional de `--observation-tests` habilita la comprobación
con router real. Sin él se ejecutan los fixtures y procesos reales del runner.
Para Linux se usaron copias aisladas de las mismas fuentes sin `bin/obj`, evitando
compartir intermedios con Windows. En un checkout Linux aislado equivalente:

```sh
TestRoot=$(mktemp -d)
dotnet publish src/Coquena -c Release -r linux-x64 -p:CppCompilerAndLinker=gcc -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r linux-x64 -p:PublishAot=true -p:CppCompilerAndLinker=gcc -o "$TestRoot/tests"
"$TestRoot/tests/Coquena.Tests" --observation-tests
COQUENA_HOME="$TestRoot/home" "$TestRoot/product/coquena" ps
```

### Hallazgos para evaluación

- La agregación es secuencial. El presupuesto HTTP limita cada instancia, pero
  el tiempo total crece con los servicios conocidos, además de lecturas locales
  y muestras del sistema. Es una limitación comprobable del recorrido finito,
  no evidencia de rendimiento a escala. Frecuencia y concurrencia requieren
  evaluación al reutilizar las muestras en 064/067 y las interfaces de M4.
- Las fuentes no son intercambiables: memoria privada, residente, dedicada y
  compartida, y las ventanas de GPU, mantienen significados distintos. No se
  publican porcentajes de VRAM sin capacidad acreditada ni consumo GPU por
  servicio sin una fuente atribuible. La prueba WSL no resuelve cobertura nativa
  de todos los fabricantes; se conserva esa limitación para la revisión.

No se realizó inferencia, benchmark, prueba de gran escala, dashboard integrado
ni suite general completa. La aceptación integrada de M3 no se acredita aquí.
