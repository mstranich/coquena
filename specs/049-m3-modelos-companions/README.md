---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
- 047-m3-transferencias
created_at: 2026-10-21T23:26:05.100040400Z
updated_at: 2026-10-27T02:06:59.292099400Z
completed_at: 2026-10-27T02:06:59.292099400Z
transitions:
- status: in-progress
  at: 2026-10-27T00:41:40.212678400Z
- status: complete
  at: 2026-10-27T02:06:59.292099400Z
---
# Resolver inventario y adquisición de modelos y companions por ámbito

## Objetivo

Adaptar selección, adquisición y catálogo de archivos de pesos a recetas y ámbitos, sin gestionar su residencia en el backend.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md#model](../../docs/comandos.md#model)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Resolver models/companions por claves independientes del name raíz; conservar rol, identidad canónica, commit, cuantización completa y shards como unidad.
- [x] Adaptar consultas/listados/descargas y retirar recursos propios de forma recuperable, sin inferir propiedad por ubicación ni adoptar externos.
- [x] Separar descarga explícita protegida de recursos automáticos requeridos; descargar no crea preset ni modifica intención por sí solo.
- [x] Mantener bindings por identidad y rutas por revisión física; no equiparar un modelo del inventario con una publicación de preset.
- [x] Exponer progreso conocido sin iniciar transferencias y respetar el camino de cuantización inequívoca entregado por 043 cuando esté disponible; su aceptación integrada corresponde a 068/069.
- [x] Conservar archivos activos, disponibilidad barata y metadatos; los recursos locales aportados se completan en 050.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)
- [047: Aislar transferencias y publicación física por ámbito](../047-m3-transferencias/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sólo archivos, selección e inventario. Sin comandos para gestionar modelos residentes, biblioteca visual o deduplicación entre proyectos.

## Estado y evidencia

Implementación revisada y aprobada por el usuario; tarea completa. Cierre en LeanSpec y entrega de todos los archivos relacionados en un único commit autorizados expresamente. Se conservan las limitaciones y el fallo preexistente documentados en la evidencia.

### Decisiones aprobadas y documentación

- Las claves de models/companions son aliases. Dos claves que resuelven la misma selección concreta comparten UUID de recurso e instalación. Se conservan el nombre canónico y sus revisiones físicas por separado del nombre raíz de receta.
- El usuario aprobó permitir selectores canónicos remotos completos sin prefijo en args de presets, sin declaración previa de alias. Es una nueva regla del producto, documentada en formato-proyecto, catálogo de parámetros, modelos y comandos. Su resolución de argumentos corresponde a 050, cuyo trabajo se amplió expresamente; no se acredita esa funcionalidad como implementada aquí. Los aliases tipados mantienen model:// y companion:// en singular, y literal conserva texto explícito.
- Persistencia documenta la tabla local opcional de aliases por UUID. La baja deja referencias rotas: reutilizar un nombre canónico no reconecta IDs retirados.

### Implementación y alcance

- ScopedModels integra selección remota por cuantización o archivos explícitos, revisión/commit, publicación verificada de 047, inventario de principales/companions, aliases por identidad, observación barata de cabeceras/shards, avisos de tamaño/fecha y progreso conocido sin iniciar transferencias.
- La adquisición explícita promueve protección de instalaciones automáticas válidas sin repetir transferencia; la preparación declarativa mantiene origen automático. No crea presets ni escribe YAML. Unidades diferentes con igual nombre canónico y commit colisionan; una nueva revisión conserva las anteriores.
- Retirada por nombre canónico, alias preparado o UUID del ámbito. Conserva inventario pendiente antes de borrar, reintenta por archivo, registra causas y cuenta sólo bytes retirados en esa invocación. No borra vecinos ni archivos externos. Una sustitución física, incluso de iguales bytes, no se adopta: se exige volumen/FileId y se valida/elimina por el mismo handle de Windows.
- La evidencia activa por recurso/revisión y las operaciones pendientes bloquean retirada. Integración con el ciclo completo de servicios, composiciones y recuperación pertenece a 051/053/054/055; no se acredita inferencia ni publicación de presets por tener inventario.
- La CLI pública model/companion ofrece list/show/download/remove/protect/unprotect con proyecto/global, base, archivos/env y JSON. Los motores M2 conservan sus regresiones mediante API/worker del runner, siguiendo el patrón de backends; sus catálogos no se migran ni se usan como fallback. Alta externa y resolución de argumentos pertenecen a 050, edición a 071, integridad explícita a 057, novedades/upgrade a 058/059 y limpieza a 060. Sin biblioteca visual de M4 ni deduplicación entre proyectos.

### Validación ejecutada

Evidencia pública anonimizada. Estado, fixtures y ejecutables de validación se conservaron localmente en el volumen acordado; no se descargaron pesos ni backends reales. Fixtures GGUF/HTTP y procesos auxiliares no acreditan inferencia ni rendimiento con pesos reales.

- Build Release sin advertencias ni errores. Publicaciones Native AOT win-x64 de producto y runner con TreatWarningsAsErrors=true, correctas; sin paquetes nuevos.
- Modelos por ámbito: 38 comprobaciones administradas y 50 con runner/producto Native AOT. Cubren aliases equivalentes, selección inequívoca sin menú, archivos remotos explícitos, companions independientes, aislamiento proyecto/global, protección, observación sin mutación, shards incompletos, revisiones activas, retirada parcial/reintento, vecinos, sustitución de identidad y progreso conocido. El producto publicado ejecutó list/show/protect/unprotect/remove y selección global.
- Transferencias: 61 comprobaciones Native AOT, incluida interrupción real de proceso y reanudación recuperable. Backends por ámbito: 56; ayuda: 17 errores sin efectos en cada camino, correctos.
- Selección remota existente: 183 comprobaciones HF y 217 de resolución, sin pesos. Motores M2 mediante runner Native AOT: adquisición/publicación 314, externos 44, refresh/integridad 28, matching 24, update 49, upgrade 101 y retirada 39, correctos. Estos procesos ejercitan API M2 retenida, no sus antiguos comandos como superficie pública del producto.
- Regresiones administradas con apphost: persistencia/identidad y recuperación entre procesos, recetas 314 y argumentos 160, correctas. Invocar esos escenarios con dotnet en vez del apphost no permite relanzar sus workers; no se registra ese error de invocación como fallo funcional.
- Revalidación final del runner Native AOT: modelos 50, argumentos 160 y persistencia/identidad con recuperación entre procesos, correctos.
- Documentación: 12 diagramas PlantUML, 4 ejemplos JSON y enlaces locales de docs, correctos. Mapa: 125 rutas existentes. git diff --check correcto. LeanSpec board/search/view revisados; deps continúa sin implementar en el adaptador, sin atribuirle validación.
- Suite general Native AOT: no está verde. Pasó CLI, contratos, ayuda, retirada, configuración, parámetros, unidades, retención, servidores/procesos, persistencia, recetas, modelos/backends por ámbito, descargas, las 61 comprobaciones de transferencias y las regresiones de backends; se detuvo en MtpResolutionTests con `037: one unit plus README/cancel`. La misma aserción se reprodujo ejecutando --mtp-tests mediante apphost sobre una exportación limpia de HEAD `ab85dfac3059150b450755159c7cc5a6e6fd7ffc`, sin cambios de 049. Se acredita como fallo preexistente; no se modificó esa política ni se amplió el alcance para corregirla. El único cambio en ese archivo enruta sus antiguos procesos de model al worker de API M2; la aserción que falla precede a ese recorrido.

### Aspecto para evaluación en revisión

Instalaciones antiguas sin identidad física por archivo no autorizan retirada por inferencia a partir de ruta o hash. La implementación conserva el pendiente con file-ownership-uncertain, como con una sustitución detectada. Es una restricción deliberada de propiedad, no evidencia de corrupción de los bytes. Evaluar si merece un recorrido explícito de recuperación; no se agregó migración, registro de hallazgos ni otra tarjeta. Limpieza y servicios deben conservar el bloqueo de retirada pendiente y las referencias por UUID al integrarse.

### Reproducción

Definir `$TestRoot` como carpeta de validación local en el volumen acordado, desde la raíz del repositorio:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet build tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-model-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scope-transfer-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-backend-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --model-engine-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" "$TestRoot/product/coquena.exe"
git diff --check
```
