---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 042-unidades-legibles
- 044-m3-contextos-persistencia
created_at: 2026-10-21T23:26:04.956876600Z
updated_at: 2026-10-23T14:57:22.562562800Z
completed_at: 2026-10-23T14:57:22.562562800Z
transitions:
- status: in-progress
  at: 2026-10-23T13:47:47.203608400Z
- status: complete
  at: 2026-10-23T14:57:22.562562800Z
---
# Aislar transferencias y publicación física por ámbito

## Objetivo

Reutilizar las garantías de transferencia M2 con operaciones, preparación y propiedad aisladas por ámbito.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [descargas.md](../../docs/descargas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Resolver download y GUID/checkpoints en el ámbito seleccionado; impedir ejecutores duplicados y efectos sobre otro inventario.
- [x] Conservar selección/commit, rangos y continuidad verificada, pausa, reanudación y cancelación sólo de preparación propia.
- [x] Verificar unidades completas y artefactos de backend antes de publicar, preservando revisiones activas y recursos externos.
- [x] Separar publicación física de instalaciones del commit de resoluciones de una combinación; exponer progreso humano legible y datos exactos estructurados.
- [x] Probar contención, interrupciones, cambio remoto, fallos de extracción/integridad/publicación y recuperación sin adopción de bytes no acreditados.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin scheduler residente, activación de procesos ni publicación parcial de un upgrade conjunto.

## Estado y evidencia

Implementación autorizada el 2026-10-23. Permanece `in-progress`, pendiente de revisión y autorización de cierre/commit.

### Implementación

- `ScopeTransfers` recibe una selección fijada y separa transferencia, preparación, publicación física e incorporación al inventario de `ScopeStore`. No escribe resoluciones de combinaciones ni cambia recetas.
- Reutiliza el motor M2 de rangos, control y checkpoints mediante almacenamiento del ámbito. Los checkpoints usan envoltura `operation`, formato 1, revisión monotónica y scope-id; una operación copiada de otro ámbito se rechaza conservando evidencia. No se adoptan catálogos M2.
- El lease del ejecutor sigue siendo un handle del SO; además se conserva PID, inicio e identidad del ejecutable como diagnóstico local. Los escritores de checkpoints y del inventario se coordinan por separado. No se retiene el bloqueo del inventario durante transferencia o hashing.
- SHA-256 cubre el prefijo de cada checkpoint y la transferencia completa. Una diferencia de longitud o de contenido del prefijo impide anexar datos. Los snapshots del hash incremental no requieren releer todo el archivo en cada checkpoint.
- Cada instalación física ocupa un slot UUID nuevo. El manifiesto conserva selección, inventario de archivos, tamaños, hashes e identidad física del directorio preparado. La recuperación comprueba esa evidencia; incluso un destino sustituido por archivos de contenido idéntico se rechaza si pertenece a otro directorio físico.
- Modelos y companions se validan como unidad GGUF completa. Los backends se extraen con límites conjuntos por operación y rutas seguras; se exigen el ejecutable y los archivos requeridos declarados por quien resolvió la selección. Esto verifica preparación física, no compatibilidad de hardware ni inferencia.
- El commit del inventario conserva instalaciones anteriores y los demás documentos de la generación. Cancelar sólo elimina preparación propia no incorporada; después del commit conserva la instalación y permite reintentar limpieza.
- `download` selecciona proyecto actual, `--base`, `--file` (alias `-f`) o `--global` (alias `-g`); el GUID nunca se busca en otro ámbito. `--json` conserva datos exactos; la presentación humana reutiliza las unidades legibles. Se actualizó la ayuda y el mapa de código.

### Validación ejecutada

Entorno de evidencia anonimizado; no se publican rutas reales ni características del equipo. Recursos pequeños locales y transportes de prueba, sin descargar pesos ni backends de producción.

- Compilación Release y publicación Native AOT win-x64 de producto y runner con warnings como errores: correctas, sin advertencias ni paquetes nuevos.
- Suite específica administrada, incluidos los alias de selección solicitados: 57 comprobaciones. Suite Native AOT con los cuatro selectores contra el producto publicado: 61 comprobaciones correctas.
- Aislamiento de GUID entre dos proyectos y ámbito global, cancelación independiente, conservación de revisiones anteriores, referencia externa protegida y ausencia de locks de combinaciones.
- Rangos 206, respuesta 200 al reanudar, cambio remoto, hash erróneo, diferencia de longitud, sustitución del prefijo sin cambiar longitud, GGUF inválido y shards incompletos.
- Ejecutor duplicado, cancelación con bytes escritos mientras el inventario está bloqueado y continuación posterior sin pérdida de checkpoint. Las regresiones M2 de contención y rangos también se ejecutan.
- Fallos inyectados y cierres reales de proceso en `Prepared`, `PhysicalPublished` e `InventoryPublished`, con recuperación idempotente; destino existente sustituido; extracción insegura; biblioteca requerida ausente y limpieza bloqueada tras publicar.
- La regresión de CLI comprueba que una operación M2 en la home no se adopta como operación de proyecto. Las pruebas de recuperación M2 conservadas se ejecutan mediante su API dentro del runner nativo; ya no presentan ese camino como el comando público M3.
- Suite general Native AOT: pasó contratos, ayuda, configuración, persistencia, recetas, descargas (55 comprobaciones), contención, instalación/publicación de backends y sus ciclos de actualización/directorios. Se detuvo en `MtpResolutionTests`, aserción `one unit plus README/cancel`: espera el menú incluso para una cuantización explícita inequívoca. El mismo fallo se reprodujo en una copia limpia de `HEAD` (`34afb03`) mediante `--mtp-tests`; no se cambió esa prueba ni el selector en esta tarea. La suite general no se presenta como verde.
- Se ejecutaron por separado las suites posteriores relevantes con el runner Native AOT: descarga/publicación de modelos (314), directorios de modelos (66), externos (44), refresh/integridad (28), matching (24), selección HF (183), resolución (217), update (49) y upgrade (101): todas correctas.
- Referencias locales de la tarjeta y mapa de código, y `git diff --check`: correctos. No se añadieron diagramas PlantUML ni ejemplos JSON documentales que requiriesen validación adicional.

Reproducción desde la raíz del repositorio, definiendo `$TestRoot` como una carpeta de pruebas en el volumen acordado:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --scope-transfer-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" "$TestRoot/product/coquena.exe"
git diff --check
```

### Límites e integración

- La selección desde YAML y la adaptación de la adquisición de backends/modelos corresponden a 048/049; las resoluciones de combinaciones corresponden a 051. Los comandos actuales de adquisición M2 todavía producen operaciones en su home; el nuevo comando público `download` no las migra ni las encuentra por fallback. Este punto debe revisarse como transición de la superficie CLI antes del recorrido integrado de M3.
- Los modelos mantienen el parcial mientras se prepara una copia verificada. Esto aumenta temporalmente el espacio y las lecturas de disco; no se midió rendimiento con pesos grandes. Una optimización debe preservar continuidad y recuperación y no constituye trabajo añadido a esta tarjeta.
- Los fallos de E/S y publicación se inyectaron con fixtures y bloqueos reales. No se agotó físicamente el espacio del disco ni se acreditó compatibilidad de un backend real o de una interfaz TUI.
- El fallo MTP previo limita la señal de la suite general. Se informa para evaluación de su relación con la descarga directa de 043; no se corrige ni se crea otra tarea por iniciativa de esta entrega.
- A solicitud del usuario se incorporó `-g` como alias de `--global` en la documentación de comandos y en `download`; `--file` ya estaba aceptado y se explicitó su alias `-f` en la ayuda. Es una ampliación aprobada de la sintaxis, sin cambiar propiedad ni selección de ámbito. No se modificaron diagramas. Sin commits, cierre, release, tag o push implícitos.
