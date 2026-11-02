---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 054-m3-recuperacion
created_at: 2026-10-21T23:26:06.006135900Z
updated_at: 2026-11-02T15:29:40.951578500Z
completed_at: 2026-11-02T15:29:40.951578500Z
transitions:
- status: in-progress
  at: 2026-11-02T15:03:02.669244200Z
- status: complete
  at: 2026-11-02T15:29:40.951578500Z
---
# Configurar logs y notificar fallos de inicio tras recuperación

## Objetivo

Conservar diagnóstico de cada ejecución y emitir sólo las notificaciones configuradas.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md#hook-de-fallo-de-inicio](../../docs/servidores.md#hook-de-fallo-de-inicio)
- [observabilidad.md#logs](../../docs/observabilidad.md#logs)
- [formato-proyecto.md](../../docs/formato-proyecto.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Resolver destinos globales/propios/externos y nombres por instancia; default pertenece al estado del servicio, no al executable externo.
- [x] Aplicar retención global y override del servicio, protegiendo activo/último y mostrando excesos de protegidos sin prometer cuota estricta.
- [x] Implementar service.server.hooks.start-failed command/args, contexto por stdin y plazo conforme al canónico; no ejecutar shell ni filtrar secretos.
- [x] Notificar una vez después del intento de recuperación, incluso cuando restaura servicio; distinguir candidata, resultado de recuperación y disponibilidad final.
- [x] Probar fallo de hook sin revertir recuperación, integridad fatal, doble fallo, primer arranque y ausencia de notificación configurada.

## Dependencias

- [054: Recuperar snapshots funcionales sin reinicios implícitos de consumidores](../054-m3-recuperacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor residente, crear usuarios/privilegios o implementar seguimiento continuo/historial, que corresponde a 062.

## Estado y evidencia

Implementación y validación ejecutadas el 2026-11-02; revisión y cierre aprobados. Commit único de la entrega. Sin push, tag ni release.

### Decisiones acordadas

- Retirar únicamente logs registrados del servicio, verificando identidad física al borrar; nunca adoptar otros archivos por su nombre UUID.
- Emitir una sola notificación al finalizar la operación, incluidos reintentos interactivos; identificar la última candidata fallida y conservar diagnóstico local de las anteriores.
- Los documentos funcionales incorporan esos criterios y explicitan la protección de valores interpolados y de la salida del hook, conforme al contrato de privacidad.

### Implementación comprobada

- Destino YAML relativo a la base o absoluto; default en el estado del servicio propio/global. Nombre tomado del ID de la instancia efectivamente iniciada, también al recuperar.
- Registro local por servicio con ID de instancia, ruta, inicio e identidad física. Retirada por handle con comprobación de identidad, conservando activo/último y diagnosticando excesos o archivos no retirables.
- Preferencia global existente y override YAML; política efectiva conservada en el snapshot de última buena. Cambiar la preferencia no lanza limpieza residente.
- Hook directo, argumentos separados, JSON DOM por stdin, plazo de 10 s y drenado de stdout/stderr con buffer fijo sin conservar su contenido. Fallo diagnosticado sin revertir recuperación. Sin nuevos paquetes.

### Evidencia reproducible

Entorno anonimizado: Windows win-x64, fixtures locales de salud y notificación, sin modelos ni descargas. Las rutas reales de evidencia quedan fuera de Git. Definir `$TestRoot` como una carpeta de pruebas en D: y `$Native` como su subdirectorio de publicación.

```powershell
dotnet build tests/Coquena.Tests --no-restore
dotnet publish src/Coquena -c Release -r win-x64 -o $Native
dotnet run --project tests/Coquena.Tests --no-build -- --service-log-hook-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests --no-build -- --service-recovery-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests --no-build -- --scoped-service-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests --no-build -- --project-persistence-tests
git diff --check
```

- Build y publicación Native AOT: correctos, sin advertencias ni errores.
- Logs/hooks: **32 comprobaciones**, incluyendo producto publicado para servicio propio y global importado, backend externo, destinos predeterminados/externos, precedencia de retención y hook tras recuperación. API: primer arranque, integridad fatal, doble fallo, reintentos, fallo/timeout de hook, ausencia de configuración, rechazo de secretos interpolados, archivo ajeno y reemplazo de un log registrado.
- Recuperación: **32 comprobaciones**, incluyendo CLI Native AOT, YAML malformado, snapshots exactos, integridad y conservación de instancias ajenas.
- Servicios: **35 comprobaciones**, incluyendo CLI Native AOT. La publicación utilizada ya contenía la implementación de logs/hooks; la publicación final se volvió a comprobar con las dos suites anteriores.
- Persistencia: regresión específica de contextos, identidad, registro, copia, índice, referencias, corrupción y recuperación entre procesos, correcta.
- Enlaces locales: **13** referencias Markdown y **250** rutas del mapa verificadas. No se modificaron PlantUML ni ejemplos JSON. Payloads JSON comprobados por las pruebas.
- Board/search/view revisados; dependencia 054 completa. La CLI informa que `deps` y `tokens` todavía no están migrados al adaptador; dependencia contrastada con metadatos y milestone.

### Limitaciones

No se acredita inferencia, carga masiva, cuota estricta de disco ni aceptación integrada de M3. El registro se recorre por servicio y crece con los logs conservados; no se ensayó una cardinalidad elevada de archivos pequeños. Un YAML inválido no proporciona configuración confiable del hook; la recuperación conserva su diagnóstico local. Seguimiento/historial de CLI y visualización corresponden a 062 y a las entregas visuales del milestone.
