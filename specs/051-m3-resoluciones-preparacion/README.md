---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 046-m3-argumentos-schema
- 048-m3-backends
- 050-m3-archivos-locales
created_at: 2026-10-21T23:26:05.250489100Z
updated_at: 2026-10-27T15:45:16.760063Z
completed_at: 2026-10-27T15:45:16.760063Z
transitions:
- status: in-progress
  at: 2026-10-27T15:12:53.471852600Z
- status: complete
  at: 2026-10-27T15:45:16.760063Z
---
# Persistir combinaciones y preparar recursos con resoluciones reproducibles

## Objetivo

Preparar una combinación de receta sin resolver novedades ajenas ni acreditar ejecución por completar archivos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Calcular identidad canónica de combinación y mantener índices compartibles/locales sin descubrir archivos por barrido.
- [x] Persistir resoluciones portables separadas de inventario, intención expandida local y snapshot de última buena; no filtrar secretos ni hashes de secretos a documentos compartibles.
- [x] Reutilizar resoluciones de intención sin cambios y resolver sólo cambios explícitos/faltantes; no exigir update para preparar una edición válida.
- [x] Separar recursos propios del servicio y del consumidor; preparar sólo referencias habilitadas y proteger las cruzadas entre ámbitos.
- [x] Publicar el conjunto preparado con generaciones y control de revisión, sin mezclar revisiones ni sobrescribir activos; una preparación no promueve última buena.
- [x] Probar concurrencia, cierre antes/después de commit, recursos existentes, pérdida de home y traslado con estado local conservado.

## Dependencias

- [046: Validar argumentos y emitir CLI e INI con schema optativo](../046-m3-argumentos-schema/README.md)
- [048: Resolver backends de servicios globales y propios](../048-m3-backends/README.md)
- [050: Reutilizar pesos locales y resolver referencias tipadas sin redescarga](../050-m3-archivos-locales/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin arrancar servicios, aplicar presets ni implementar recuperación de proceso. No exige init o editor CLI.

## Estado y evidencia

Implementada y validada; permanece in-progress para revisión y aprobación del usuario. Sin commit.

### Decisión aprobada

Para selecciones que no pueden relativizarse por pertenecer a otra unidad, el usuario aprobó calcular un ID exclusivamente local con la ruta absoluta normalizada. Se concretó esa excepción en persistencia-proyectos.md; no se publica como identidad portable.

### Implementación

- `CombinationIdentity` calcula SHA-256 del JSON canónico de selecciones ordenadas, con UTF-8 y Unicode literal, incluidos caracteres suplementarios. Mantiene capitalización y diferencia identidad textual de identidad física. Los índices sólo registran selecciones invocadas explícitamente.
- `CombinationPreparation` conserva índices y resoluciones en generaciones de `ScopeStore`. El índice local guarda snapshot expandido, bindings físicos, intento/revisión de preparación y HMAC por intención con clave aleatoria local. La consulta de combinaciones no expone snapshots ni huellas. Los documentos portables conservan expresiones originales, selecciones concretas e identidades de contenido, sin rutas físicas ni UUID locales. Variables que seleccionan recursos concretos mantienen esa resolución en el área local.
- `ScopedCombinationResources` integra adquisición y reutilización de backend, modelos y companions. Selecciones sin cambios usan la resolución exacta, sin consultar releases ni metadatos remotos. Una edición sólo vuelve a resolver los recursos afectados; un lock recibido sin inventario local permite preparar su selección exacta. La pérdida de clave invalida las huellas locales, sin declarar corruptos los recursos.
- Sólo se preparan referencias de bloques habilitados, incluidas listas y selectores canónicos directos; el wrapper literal conserva texto. Los recursos declarados sin uso no se adquieren. Externos siguen en su ubicación; recursos del consumidor permanecen en su ámbito. El coordinador acepta el ID de destino ya resuelto por el llamante y registra protecciones cruzadas en un commit conjunto, comprobando la revisión del destino.
- Un modelo gestionado cuyo archivo desapareció puede prepararse de nuevo con la selección fijada, en otro slot, sin sobrescribir el anterior. No se omiten fallos de integridad conocidos. Un backend `latest` resuelto no se transforma en pin explícito; los pins declarados conservan su protección.
- Se releen bytes de YAML/env/schema y lock importado bajo el bloqueo de publicación; cambios de intención, intento concurrente o revisión del destino producen conflicto. Una interrupción anterior al commit conserva el conjunto previo; una posterior reconcilia la decisión durable sin repetir red. Se conservan generaciones y evidencia anteriores. Ninguna preparación escribe active ni promueve last-good.
- Las APIs preparan el conjunto para los llamantes de ciclo/publicación de 053–055; no se agrega un verbo CLI de preparación, ni se anticipan inicio de servicios, importación, edición YAML, update/upgrade o limpieza. El mapa de código incluye el recorrido nuevo. No se agregaron paquetes.

### Validación ejecutada

Entorno de evidencia pública anonimizado; ubicaciones y salidas originales sólo locales. Fixtures y estado en el volumen acordado. No se descargaron pesos o backends reales ni se acredita inferencia.

- Build Release de la solución: correcto, cero advertencias y errores. Producto y runner Native AOT win-x64 publicados con `TreatWarningsAsErrors=true`.
- Combinaciones: 59 comprobaciones administradas y 59 Native AOT. Incluyen identidad exacta con Unicode, orden/mayúsculas, selección entre unidades, privacidad, referencias habilitadas, lock portable sin estado/home anterior, HMAC y pérdida de clave, cambios selectivos, recursos locales y remotos, `latest` sin consulta de novedades, pins, reposición exacta de archivo ausente, protección, traslado físico y concurrencia.
- Recuperación: fallos inyectados en las cuatro etapas de publicación local; decisiones conjuntas consumidor/destino antes/después de commit y tras publicar un participante; conflictos del destino. Además, terminaciones reales de procesos en `Prepared`, `Decided` y `ParticipantPublished`, con reconciliación posterior e idempotencia.
- Regresiones administradas: recetas 314, argumentos 160, modelos locales 69, modelos por ámbito 41, transferencias 57, backends por ámbito 49 y persistencia/identidad/recuperación entre procesos, correctas.
- Regresiones Native AOT: recetas 314, argumentos 160, modelos locales 83, modelos por ámbito 53, transferencias 61, backends por ámbito 56 y persistencia/identidad/recuperación entre procesos, correctas. Ayuda/aliases y 17 errores sin efectos, correctos en ambos caminos; el producto publicado ejecutó `--help` correctamente.
- Enlaces locales y ejemplos JSON documentales, 148 rutas existentes del mapa y `git diff --check`, correctos. No se modificaron diagramas PlantUML.
- Board/search/view y dependencias revisados. `leanspec deps` continúa sin migrar en el adaptador; no se le atribuye validación. No se ejecutó la suite general ni se acredita aceptación integrada de M3.

### Consecuencias para revisión

La adquisición física y el conjunto preparado son publicaciones diferentes: un recurso adquirido antes de un fallo puede permanecer inventariado, mientras la resolución de combinación conserva su conjunto anterior. Esa conservación aplica la política canónica de no borrar implícitamente; limpieza debe considerar generaciones, combinaciones y referencias cruzadas. No se crean tarjetas ni registros adicionales por esta observación.

### Reproducción

Definir `$TestRoot` como carpeta externa al repositorio en el volumen de validación acordado, desde la raíz del checkout:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet build Coquena.slnx -c Release
dotnet run --no-build --project tests/Coquena.Tests -c Release -- --combination-tests
dotnet publish src/Coquena -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --combination-tests
& "$TestRoot/tests/Coquena.Tests.exe" --argument-tests
& "$TestRoot/tests/Coquena.Tests.exe" --recipe-tests
& "$TestRoot/tests/Coquena.Tests.exe" --project-persistence-tests
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-local-model-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-model-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scope-transfer-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-backend-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```
