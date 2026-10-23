---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
- 047-m3-transferencias
created_at: 2026-10-21T23:26:05.030887800Z
updated_at: 2026-10-23T16:53:28.938176400Z
completed_at: 2026-10-23T16:53:28.938176400Z
transitions:
- status: in-progress
  at: 2026-10-23T16:02:53.194833200Z
- status: complete
  at: 2026-10-23T16:53:28.938176400Z
---
# Resolver backends de servicios globales y propios

## Objetivo

Preparar el backend del único servicio definido por una receta o registrado globalmente, conservando propiedad y selección explícita.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [formato-proyecto.md#backend](../../docs/formato-proyecto.md#backend)
- [servidores.md](../../docs/servidores.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Resolver sólo oficial version/variant o executable externo; backend completo reemplazable por override, sin repositorio configurable ni sustitución automática de familia.
- [x] Guardar intención y revisiones físicas por ámbito; usar gestionados globales en la home y gestionados propios en el proyecto.
- [x] Usar ejecutables externos relativos/absolutos in situ y conservar base de origen tras importación, sin copiar bibliotecas ni adoptar propiedad.
- [x] Reutilizar instalación válida y preparar candidatos separados del activo; diagnosticar hardware incompatible sin bloqueo de integridad inventado.
- [x] Adaptar backend/runtime y operaciones de recursos al contrato CLI; no confundir selección de backend con alias default de servicio.
- [x] Preservar pin y variante y la protección del build fijado al cambiar intención; integrar novedades/upgrade en sus entregas específicas.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)
- [047: Aislar transferencias y publicación física por ámbito](../047-m3-transferencias/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin migración automática M2, edición general de YAML, reinicio al seleccionar backend ni proveedores nuevos.

## Estado y evidencia

Implementación revisada y aprobada por el usuario; tarea completa. Todos los archivos se entregan en un único commit.

### Decisión de comportamiento aprobada

El usuario indicó detener la preparación ante incompatibilidad detectable por System Probe y permitir intentar con `--force`, conservando la advertencia. Se concretó en `docs/servidores.md` y `docs/comandos.md`: la autorización es por invocación, también al reanudar; no cambia variante ni omite integridad, plataforma soportada, archivos requeridos o sondeo de versión. Evidencia insuficiente sigue siendo `unknown`, con advertencia, sin inventar incompatibilidad. Es una decisión nueva del producto, no una adaptación silenciosa de las reglas al código.

### Implementación y límites de integración

- `ScopedBackends` recibe intención y servicio registrado. Conserva intenciones/preparaciones locales por identidad de servicio, separadas del inventario físico. Las revisiones gestionadas usan los slots inmutables y la publicación recuperable de 047. No se escribe el estado activo ni se promueve última buena.
- La misma intención `latest` preparada reutiliza su revisión sin buscar novedades. Una instalación completamente ausente se prepara con el build/variante exactos y se rechazan cambios de artefactos. Faltantes parciales y fallos de integridad conocidos no se reparan por inferencia.
- Cada pin preparado obtiene protección local; cambiar intención o variante conserva revisiones anteriores. Protect/unprotect cambia esa protección sin borrar archivos ni retirar referencias independientes. El historial local facilita reutilización; los locks y referencias por combinación corresponden a 051, y limpieza debe integrar esas referencias y protecciones en 060.
- Externos no se copian ni se someten al formato `--version` oficial. En proyecto, una ruta relativa se conserva relativa; una importación global recibe la base original y registra el ejecutable absoluto. El servicio no toma un nombre nuevo ni cambia el alias default por seleccionar backend.
- CLI pública: `backend` y `runtime` comparten show/list/download/protect/unprotect, selección de proyecto/global, composición manual de archivos y salida estructurada. La descarga explícita prepara/protege sin editar YAML. Los verbos M2 dejan de ser el contrato público; sus motores conservan pruebas mediante API dentro del runner, sin migración ni fallback de catálogos.
- Set/edición puntual pertenece a 071, resoluciones conjuntas a 051, control de proceso a 053, importación integrada a 056, novedades/upgrade a 058/059 y retirada/limpieza a 060. La 048 ofrece la preparación reutilizable para esos recorridos; no acredita el flujo integrado de esas entregas ni anticipa interfaces M4.
- El validador del backend oficial comprueba `--version` antes del commit del inventario, también al reanudar una publicación interrumpida. Reutilizar comprueba archivos/tamaños, disponibilidad, fallo de integridad conocido y versión; no sustituye la auditoría completa de verify.

### Validación ejecutada

Entorno de evidencia pública anonimizado; hardware y ubicaciones reales se conservan sólo localmente. Las pruebas de selección usan hardware y artefactos sintéticos. No se descargaron backends reales ni se acredita inferencia o compatibilidad de un build real.

- Build Release: correcto, sin advertencias ni errores. Publicaciones Native AOT `win-x64` de producto y runner con `TreatWarningsAsErrors=true`: correctas.
- Backends por ámbito: 49 comprobaciones administradas y 56 con producto/runner Native AOT. Incluyen latest sin red, cambios de pin/variante, protección, instalación ausente, integridad inválida, fuerza explícita, evidencia desconocida, reanudación sin bypass de hardware, versión inválida, instancia registrada preservada, aislamiento de proyectos/global, externos sin adopción, base original importada y traslado del proyecto.
- La prueba nativa reanuda una transferencia preparada con un ejecutable fixture real: valida `--version`, publica inventario y completa limpieza sin red. También ejecuta show/list/protect/unprotect del producto publicado.
- Transferencias: 57 comprobaciones administradas y 61 Native AOT; ayuda: 17 errores sin efectos y aliases correctos en ambos caminos; regresiones de recetas: 314 y argumentos: 160, correctas.
- Regresiones de motores de backends dentro del runner Native AOT: instalación/reinstalación 57, publicación con lectores Windows 43, externos 32, update 52, upgrade/retención 51 y directorios 105, correctas. Los comandos M2 se ejercitan mediante un worker de API del runner; no se presentan como comandos públicos vigentes del producto.
- `system probe` ejecutado mediante el producto publicado; acredita el sondeo local, no la compatibilidad de un backend ni inferencia. Sus datos concretos no se incorporan a evidencia pública.
- Suite general Native AOT: avanzó por contratos, ayuda, configuración, procesos, persistencia, recetas, las 56 comprobaciones de 048, selección de backends y 55 comprobaciones de descargas. Se interrumpió en una prueba de recuperación de `ScopeTransferTests`, con `FileNotFoundException` desde el monitor de controles al desaparecer el directorio de operación. La suite general no está verde.
- Enlaces locales modificados, rutas del mapa y `git diff --check`: correctos. No se modificaron diagramas PlantUML ni ejemplos JSON documentales. Board/search/view permiten revisar estado y dependencias; `leanspec deps` y `leanspec tokens` informan que aún no están migrados al adaptador, por lo que no se atribuye validación a esos comandos.

### Hallazgo observado fuera de la implementación de 048

La ejecución general observó una carrera compatible con la comprobación de existencia seguida de `GetFileAttributes` en `ScopeContext.PathFor`, mientras `DownloadManager` mantiene el monitor hasta finalizar la limpieza. Estos dos archivos no fueron modificados en 048. La suite enfocada de transferencias pasó antes y después del fallo; esto no demuestra ausencia de la carrera ni permite afirmar que se reprodujo sobre un checkout limpio anterior. Puede convertir una finalización física en error de la invocación. No se observaron pérdidas de datos; ese riesgo no se acredita por esta excepción. Se informa para evaluar un seguimiento independiente, sin cambiar esos archivos ni crear tarjetas o registros de hallazgos automáticamente.

### Reproducción

Definir `$TestRoot` como una carpeta de validación en el volumen acordado, desde la raíz del repositorio:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet build tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-backend-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scope-transfer-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --backend-engine-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" "$TestRoot/product/coquena.exe"
git diff --check
```
