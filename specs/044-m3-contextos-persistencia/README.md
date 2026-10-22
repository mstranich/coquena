---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
created_at: 2026-10-21T23:26:04.646221500Z
updated_at: 2026-10-22T21:26:22.085117300Z
completed_at: 2026-10-22T21:26:22.085117300Z
transitions:
- status: in-progress
  at: 2026-10-22T17:20:19.237257800Z
- status: complete
  at: 2026-10-22T21:26:22.085117300Z
---
# Separar ámbitos y persistir identidad local de recetas y servicios

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo

Entregar el contexto y la persistencia comunes de proyectos y del ámbito global explícito.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Resolver base/rutas e identidad física; separar home, inventario global y estado local del proyecto según el layout canónico, sin migración implícita de M2.
- [x] Implementar documentos/versiones, generaciones, exclusión de escritores y publicación recuperable; corrupción nunca equivale a catálogo vacío.
- [x] Persistir en .coquena/local/ la referencia lógica de receta, nombre usado, entidades creadas y destino; mantener la home como índice de ubicación y ámbito importado.
- [x] Probar rename de carpeta con conservación de nombres previos, edición atómica del YAML, copia de estado local, alias de rutas y pérdida del índice de home.
- [x] Proveer registro/consulta de identidad de servicio, consumidor, instancia y recurso, separado de sus nombres y de la combinación; probar aislamiento con nombres repetidos.
- [x] Hacer recuperables las referencias entre ámbitos y la coordinación con el registro local de recetas; preservar propiedad y evidencia ante identidad incierta.

## Dependencias

Sin bloqueos de implementación dentro de M3. Referenciar documentación no crea una dependencia.

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Infraestructura de contexto y persistencia, no compositor, importación, comandos de ciclo ni deduplicación global.

## Estado y evidencia

Completada con autorización del usuario del 2026-10-22; commit único de la entrega.

- Núcleo en `src/Coquena/Projects/`: contexto de proyecto/global, identidad física Windows, envolturas JSON, generaciones, exclusión entre procesos, coordinación recuperable, registro local e índice de navegación. Sin paquetes nuevos ni migración M2. No incorpora comandos públicos ni el compositor YAML.
- `ProjectPersistenceTests` comprueba rename, reemplazo atómico de YAML, junctions, copia sin adopción de propiedad, pérdida del índice, nombres repetidos, alias default, recursos/instancias, referencias entre ámbitos, colisiones ajenas y corrupción conservada.
- Interrupciones reales de proceso en prepared, decided, participant-published y projections-published: antes de decisión se conserva la revisión anterior; después se reconcilia el grupo. Una generación comprometida corrupta impide exponer el resultado y conserva los marcadores.
- Concurrencia con revisión compartida en dos procesos independientes: exactamente un commit y un conflicto; además se comprueba writer-busy y concurrencia dentro del proceso.
- Publicados producto y runner de pruebas Native AOT win-x64 con warnings como errores en `${TestRoot}/coquena-044/product` y `${TestRoot}/coquena-044/tests`. Pruebas específicas del runner nativo: OK. Evidencia conservada en `${TestRoot}/coquena-projects-run`.
- Reproducción: establecer TEMP y TMP en `${TestRoot}`; `dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 --self-contained true -o ${TestRoot}/coquena-044/product -p:PublishAot=true -p:TreatWarningsAsErrors=true`; publicar análogamente `tests/Coquena.Tests/Coquena.Tests.csproj` en `${TestRoot}/coquena-044/tests`; ejecutar `${TestRoot}/coquena-044/tests/Coquena.Tests.exe --project-persistence-tests`.
- Documentación: `project-index` concreta el tipo del índice de ubicación, distinto del registro que acredita propiedad. Comprobados enlaces locales, ejemplos JSON y `git diff --check`. No se modificaron diagramas PlantUML.
- La suite general debe ejecutarse con el runner Native AOT y la ruta del producto publicado: un intento con runner administrado se detuvo en InstallerTests 55 porque el fixture extraído necesita un ejecutable autocontenido. No se atribuye ese resultado a una regresión del producto.
- Suite general nativa: `${TestRoot}/coquena-044/tests/Coquena.Tests.exe ${TestRoot}/coquena-044/product/coquena.exe` pasa contratos, CLI, configuración, parámetros, unidades, retención, servidores/procesos, almacenamiento M2, persistencia de proyectos, transferencias/contención, instalación/publicación de backends, externos, update/upgrade, directorios y resolución 015. Se detiene con salida 1 en `MtpResolutionTests`: `037: one unit plus README/cancel`. Reproducido aisladamente con `--mtp-tests` sin ejecutar las nuevas pruebas de proyectos. `MtpResolutionTests.cs` y `ModelSelection.cs` no están modificados: la expectativa de menú para cuantización explícita contradice el retorno directo existente cuando hay una sola variante. No se corrige ni se crea otra tarjeta dentro de esta entrega. El resto de la suite posterior a ese punto no quedó ejecutado por este recorrido.

### Separación de alcance con 045

La consulta sobre la interpretación de varios YAML/overrides corresponde a la integración del lector/compositor de 045 y no bloquea esta infraestructura. Corregido tras la observación del usuario: Register da de alta una identidad explícita y Get consulta por recipe-id; los archivos se conservan como evidencia, sin usarlos como clave automática de identidad. La regresión verifica altas explícitas distintas con iguales archivos, consumidores independientes y consulta de una identidad previa tras rename o edición atómica. La selección/composición de archivos no se implementa ni se decide en 044.

Validación tras esta corrección: pruebas específicas administradas y Native AOT OK; producto y runner republicados win-x64 con warnings como errores; `coquena.exe --help` OK con home de prueba en D:; `git diff --check` OK. Evidencia nativa actual: `${TestRoot}/coquena-projects-run`. No se repite la suite general: el fallo MTP ya reproducido sigue siendo una limitación de su resultado.

### Límites observados para revisión

- Si un participante se mueve durante una operación pendiente y no puede localizarse por las rutas conservadas, se diagnostica y se conserva evidencia. No se recorre el disco ni se adopta otra carpeta por UUID copiado. Esto condiciona navegación/recuperación posterior; no acredita recuperación automática de un traslado durante el commit.
- La prueba nativa específica ejecuta directamente el núcleo en el runner AOT. La CLI del producto todavía no expone estas operaciones; su integración corresponde a las tarjetas de sus comandos.
