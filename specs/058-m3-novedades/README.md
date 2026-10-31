---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
created_at: 2026-10-21T23:26:05.758536300Z
updated_at: 2026-10-31T15:11:46.113834300Z
completed_at: 2026-10-31T15:11:46.113834300Z
transitions:
- status: in-progress
  at: 2026-10-31T14:05:12.294149100Z
- status: complete
  at: 2026-10-31T15:11:46.113834300Z
---
# Consultar novedades y persistir planes exactos por selección

## Objetivo

Informar novedades globales o de combinación y conservar un plan explicable sin modificar intención o pesos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Consultar origen y fijar versiones/commits/artefactos actuales y propuestos por recurso, sin descargar pesos ni actualizar otras combinaciones.
- [x] Mantener plan local ligado a intención/resolución/revisión y fecha; edición concurrente o cambio de intención invalida el plan.
- [x] Respetar cuantización, variante, propiedad y correspondencia exacta; fallo remoto o no concluyente no equivale a sin novedades.
- [x] Informar novedades aun con pin fijo sin convertirlo a latest; comprobación automática sólo informa, sin upgrade ni reinicio.
- [x] Implementar selectores de recurso y probar candidato presente, renombres remotos, ambigüedad, error y ausencia real.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin publicar recursos, editar YAML ni recalcular y aplicar silenciosamente otro plan.

## Estado y evidencia

Implementación y evidencia terminadas. La tarjeta fue cerrada como `complete`.
Commit único realizado; sin push, tag ni release.

### Comportamiento entregado

- `update` consulta los recursos utilizados por la combinación seleccionada;
  `update --global` consulta las intenciones registradas del ámbito global, sin
  recorrer proyectos ni YAML desconocidos. Requiere resoluciones de partida y
  su clave local de intención; un recurso sin correspondencia preparada se
  informa como `not-prepared`, sin fabricar una versión actual.
- `--select` usa referencias `backend://<variante>`, `backend://<alias>`,
  `backend://<proyecto>/<alias>`, `model://<clave>` y
  `companion://<clave>`, con identidades remotas completas admitidas para pesos.
  Es la decisión explícita del usuario, aplicada también a `verify` de proyecto
  y `verify --global`. El ámbito lo define el contexto, no el prefijo del alias.
  No se conserva la sintaxis anterior de esos filtros. Import mantiene sus
  selectores de categorías y reemplazo, que pertenecen a otro contrato.
- Los filtros son unión; un filtro desconocido o inválido falla sin ampliar la
  consulta. Una variante puede seleccionar varios servicios; las observaciones
  conservan la combinación de procedencia y los IDs de recurso e instalación.
- `service.backend.name` declara el alias opcional; sin él se utiliza el nombre
  efectivo de receta. Import conserva alias y calificación. Colisiones entre
  alias de distintos servicios requieren calificación y fallan antes de operar.
  Las variantes oficiales sin calificar conservan su interpretación de variante;
  un alias coincidente se selecciona mediante la forma calificada.
- Un alias puede seleccionar un backend externo para verify o uno oficial para
  update. No cambia propiedad ni origen. El externo se informa `not-applicable`
  para actualización gestionada. Cambiar sólo el alias no renueva latest ni crea
  una instalación: se mantiene la identidad física y se invalida el plan anterior.
- Verify admite filtros en proyecto, conservando la validación completa de la
  definición. Sin filtro mantiene la auditoría de todos sus recursos utilizados;
  `--include-unused` continúa siendo exclusivo de global. Un alias externo aún
  no preparado se reconoce sin certificarlo ni registrarlo por inferencia.
- El plan local conserva fecha, revisión base, intención ligada mediante HMAC,
  resolución de partida, selecciones exactas actuales/propuestas y acciones de
  reutilización/descarga por archivo de pesos. No descarga pesos, modifica YAML,
  cambia resoluciones, publica presets, reinicia ni ejecuta upgrade.
- Una consulta nueva sustituye el plan de esa selección: filtros parciales y
  errores remotos no dejan autorizadas propuestas anteriores. Los planes de
  otras combinaciones se conservan. Lectura del plan sin consultas remotas,
  invalidación por edición de YAML/env/schema, cambio de clave o preparación;
  control optimista de escritores y comprobación de entradas durante publicación.
- Pin fijo informa novedades manteniendo el pin y sin permitir upgrade del
  backend. La comprobación no interactiva sólo informa; no incorpora un timer,
  supervisor ni política nueva de comprobación periódica.
- Reubicación exacta exige identidad de contenido comparable del conjunto completo;
  ausencia confirmada, ambigüedad, error y evidencia insuficiente son diferentes.
  La elección interactiva de un candidato conserva la cuantización y no afirma
  sucesión retrospectiva. Externos no se adoptan ni se consultan como gestionados.
- El reporte CLI omite huellas privadas y redacta selecciones expandidas desde
  variables. El plan operativo exacto permanece local y excluido de Git.

### Validación

Entorno anonimizado, 2026-10-31. Definir `$TestRoot`, `$ProductRoot` y `$NativeTestsRoot`
como directorios externos al checkout en el volumen de pruebas acordado.

```powershell
dotnet run --project tests/Coquena.Tests -- --recipe-update-tests "$TestRoot/managed"
dotnet run --project tests/Coquena.Tests -- --verification-tests
dotnet run --project tests/Coquena.Tests -- --recipe-tests
dotnet run --project tests/Coquena.Tests -- --recipe-import-tests
dotnet run --project tests/Coquena.Tests -- --scoped-backend-tests
dotnet publish src/Coquena -c Release -r win-x64 -o "$ProductRoot"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -o "$NativeTestsRoot"
& "$NativeTestsRoot/Coquena.Tests.exe" --recipe-update-tests "$TestRoot/native" "$ProductRoot/coquena.exe"
& "$NativeTestsRoot/Coquena.Tests.exe" --verification-tests "$ProductRoot/coquena.exe"
& "$NativeTestsRoot/Coquena.Tests.exe" --recipe-tests
& "$NativeTestsRoot/Coquena.Tests.exe" --recipe-import-tests
& "$NativeTestsRoot/Coquena.Tests.exe" --scoped-backend-tests "$ProductRoot/coquena.exe"
& "$NativeTestsRoot/Coquena.Tests.exe" --help-tests "$ProductRoot/coquena.exe"
git diff --check
```

- Actualización: 73 comprobaciones administradas; 78 en el ejecutable de pruebas
  Native AOT, incluidas invocaciones reales del producto publicado para proyecto
  y ámbito global. Fixtures de metadatos rechazan solicitudes de pesos.
  Incluye alias oficial actualizable, alias externo verificable, forma calificada,
  colisión global sin efectos, conservación tras importación, alias predeterminado
  por receta y reutilización física al cambiar sólo el alias. Se invoca el producto
  publicado con selección de backend tanto en proyecto como en global.
- Verificación: 53 comprobaciones administradas; 57 en Native AOT con el producto
  publicado. Incluye identidad remota, alias local, rechazo de sintaxis anterior,
  selección exacta de un modelo global y detección de corrupción sin reparar.
- Regresiones de recetas: 323 comprobaciones; importación: 24 aserciones de
  selección y 71 comprobaciones integradas, correctas en administrado y Native AOT.
  Backends por ámbito: 49 comprobaciones administradas y 56 con producto Native AOT,
  correctas. No se ejecutó la suite general ni la aceptación integrada de M3.
- Publicación Native AOT win-x64 de producto y pruebas correcta, sin advertencias
  ni dependencias nuevas. Se ejecutaron los binarios publicados con datos de prueba
  en el volumen acordado.
- Se corrigieron líneas nuevas y previas de ayuda que superaban las 80 columnas.
  Ayuda, aliases y 16 errores sin efectos: correctos tanto en modo administrado
  como con el producto Native AOT reconstruido.
- Enlaces locales de los Markdown modificados: 37 destinos existentes. Mapa de
  código: 219 referencias a archivos existentes. Un ejemplo JSON validado;
  no se modificaron diagramas PlantUML. `git diff --check` correcto.
- Board/search/view revisados. `leanspec deps` y `leanspec tokens` siguen sin migrar al adaptador;
  la dependencia 051 se comprobó mediante tarjeta y board.

### Restricciones y cuestiones para revisión

La definición de alias y los filtros de verify de proyecto incorporan decisiones
expresas del usuario que amplían la gramática anterior. Se actualizaron formato,
servicios/recetas, comandos y verificación para expresar esas reglas, sin adaptar
silenciosamente los contratos a una limitación del selector anterior.

La regresión ampliada de importación detectó una expectativa anterior a los
bloqueos de integridad de modelos: la prueba sólo capturaba `StorageException`
con `resource-integrity-invalid`, mientras el preparador de modelos devuelve
`ModelSourceException` con `model-integrity-invalid`. Se ajustó exclusivamente
la captura de esos dos fallos concretos, manteniendo las comprobaciones de
conservación de intención y archivos. No cambió la política de integridad.

La pérdida de la clave local impide acreditar vigencia y requiere preparar de nuevo;
no se sustituye la clave ni se certifican resoluciones por semejanza. Los recursos
externos siguen disponibles para verificación, pero no para actualización gestionada.
Estas restricciones derivan de los contratos canónicos.

Los planes comparten un documento local sujeto al límite existente de 16 MiB por
documento. Se conserva un plan por selección conocida, no un historial de consultas.
El costo de lectura/composición y el crecimiento del número de selecciones no se
midieron a gran escala. Conviene evaluar ese límite para catálogos grandes y las
interfaces de M4; no se anticipa aquí otra persistencia ni se crea una tarea adicional.

La evidencia acredita fixtures y ejecución Native AOT; no es una prueba de
servicios remotos en vivo, rendimiento, inferencia ni aceptación integrada de M3.
Las ubicaciones originales permanecen fuera de la evidencia versionada.
