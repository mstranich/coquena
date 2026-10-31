---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 055-m3-publicacion-presets
created_at: 2026-10-21T23:26:05.613396Z
updated_at: 2026-10-30T22:01:59.616478Z
completed_at: 2026-10-30T22:01:59.616478Z
transitions:
- status: in-progress
  at: 2026-10-30T20:18:05.525158700Z
- status: complete
  at: 2026-10-30T22:01:59.616478Z
---
# Importar recetas completas o selectivas con reemplazo explícito

## Objetivo

Copiar intención seleccionada al ámbito global y preparar/publicar lo correspondiente sin seguir o modificar la receta original.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md#importación-global](../../docs/servicios-recetas.md#importación-global)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar import, categorías --select, --as-default y dependencias excluidas conforme al contrato; no incorporar categorías ocultas.
- [x] Mantener un name raíz y registros independientes; aplicar identidades de proyecto a presets y recursos locales, y promoción sin prefijo al importar globalmente, conservando identidades remotas, archivos físicos y claves de parámetros.
- [x] Probar idempotencia de intención idéntica y rechazo por defecto de conflictos; implementar --replace por entidad y --force para el archivo completo con sus incompatibilidades.
- [x] Conservar revisiones activas, pins, propiedad y referencias de otros consumidores incluso con reemplazo autorizado; --force no omite validaciones ni renombra implícitamente.
- [x] Materializar intención/schema importados y conservar base de externos sin copiar binarios/pesos; preservar expresiones y evaluarlas en cada comando, sin congelar expansiones ni copiar env a intención compartible. Última ruta conocida sólo diagnóstica.
- [x] No iniciar servicio al importar; publicar sólo presets seleccionados si está activo, o conservar para inicio si está detenido. Registrar incertidumbre/remanentes sin presentar aplicación completa falsa.
- [x] Probar import completo/selectivo, colisiones, alias default, fallo de publicación, concurrencia y ausencia de cambios en original/externos.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)

## Implementación

El comando `import` materializa intención global y schemas JSON con precedencia
por capa y cadenas literales, sin copiar externos ni modificar el origen. Conserva
expresiones y bases relativas; las resoluciones privadas por entidad reutilizan
selecciones exactas, incluso desde otra fuente o al iniciar el servicio generado.
Servicios y consumidores tienen registros independientes. La promoción elimina
el prefijo de proyecto de presets y recursos locales; no altera nombres remotos.

La generación recuperable incluye YAML, registro y alias default. Se capturan
los bytes de YAML, env y schemas y se comprueban de nuevo antes de decidir el
commit. Una exclusión no interpola ni prepara sus dependencias. Los conflictos
muestran campos e impacto sin divulgar valores; replace/force no quitan referencias,
pins ni revisiones anteriores. Un lease de operación serializa importaciones de
una home sin mantener el writer del catálogo durante interacción o HTTP.

La publicación conserva remanentes por preset y reobserva operaciones inciertas.
El servicio de una operación pendiente permanece fijado aunque cambie default.
Una respuesta perdida al restaurar tiene prioridad de incertidumbre sobre un fallo
de warmup conocido; observar la restauración no acredita la candidata pendiente.
La importación detenida conserva la composición; no inicia procesos.

## Validación y evidencia

Entorno anonimizado: Windows win-x64, fixtures y binarios en una raíz local de
pruebas en D:, excluida de Git. Sin descargas costosas ni cambios a pesos/binarios
externos. `$TestRoot` representa esa raíz; `$Product` y `$NativeTests` son los
archivos publicados; `$Backend` es un llama-server oficial b11899 disponible
localmente. `$RuntimeEvidence` es un directorio nuevo y vacío bajo `$TestRoot`.

```powershell
dotnet build tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-build -- --recipe-import-tests
dotnet publish src/Coquena -c Release -r win-x64 -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -o "$TestRoot/tests"
& $NativeTests --recipe-import-tests
& $NativeTests --scoped-local-model-tests $Product
& $NativeTests --recipe-import-runtime $Backend $Product $RuntimeEvidence
```

- Selección: 24 aserciones; integración: 71 verificaciones, tanto administradas
  como Native AOT. Incluyen import completo/selectivo, conflictos, reemplazo,
  force/integridad, alias, variables, schemas, conservación del origen,
  dependencias excluidas, concurrencia, cambios antes del commit y recuperación
  en Prepared/Decided/ParticipantPublished. También publicación parcial,
  pérdida de respuesta, warmup/restauración y reintento sin reload duplicado.
- Producto y pruebas publicados Native AOT win-x64 y ejecutados. Recorrido con
  router real: 14 verificaciones; import sin inicio, start explícito, reload,
  preservación de consumidor ajeno e instancia activa, rechazo sin interacción,
  reemplazo de backend sin reinicio y nombre estable tras renombrar el origen.
- Modelos locales: 76 verificaciones administradas y 90 en Native AOT con el producto,
  incluida identidad de proyecto, promoción,
  nombres remotos y conservación de revisiones/tipos y recorrido CLI.
- Regresión administrada: recetas 323, argumentos 160, combinaciones 69,
  publicación 41, servicios 33, recuperación 24, backends 49, modelos 41 y
  persistencia de ámbitos. Sin fallos.
- Documentación: enlaces locales y anchors, ejemplos JSON y rutas del code-map
  comprobados; sin cambios PlantUML. `git diff --check` y revisión de espacios
  en archivos nuevos sin incidencias.

## Límites y revisión

Sin importación remota de repositorios, migración M2, proveedores nuevos ni adopción
de externos. El recorrido real acredita catálogo y ciclo del servicio; no inferencia,
calidad de modelos ni rendimiento. Fixtures GGUF mínimos no acreditan carga de pesos.

La serialización por home es una restricción de concurrencia comprobada: otro
import recibe import-busy. Su granularidad merece evaluación si las interfaces
concurrentes de M4 requieren más throughput. Las resoluciones importadas añaden
referencias privadas a recurso/instalación que actualización y limpieza deben
considerar junto con las protecciones existentes; esta entrega no implementa
esas operaciones ni crea tareas adicionales.

La documentación funcional aclara las decisiones aprobadas de nombres/promoción
y variables. El documento de persistencia describe el mecanismo recuperable y las
resoluciones exactas, sin cambiar la política de propiedad ni autorizar fallbacks.
La tarjeta queda cerrada con un único commit que reúne la implementación,
pruebas y documentación de esta entrega.
