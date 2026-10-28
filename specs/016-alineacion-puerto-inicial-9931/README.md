---
status: archived
created: 2026-10-09
tags:
- m1
depends_on:
- '011'
created_at: 2026-10-09T13:41:51.558051500Z
updated_at: 2026-10-28T10:21:41.641632100Z
completed_at: 2026-10-09T19:18:58.773509100Z
transitions:
- status: in-progress
  at: 2026-10-09T18:07:58.344565700Z
- status: complete
  at: 2026-10-09T19:18:58.773509100Z
- status: archived
  at: 2026-10-28T10:21:41.641632100Z
---
# Alinear el puerto inicial de servidores con llama.cpp: 9931

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Cambiar el puerto inicial de Coquena de 8080 a 9931 para nuevas definiciones con catálogo vacío, alineándolo al cambio anunciado por llama.cpp. Mantener los puertos ya persistidos y las reglas de asignación y unicidad.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [Puertos y configuración de servidores](../../docs/servidores.md#puertos)
- [CLI de servidores](../../docs/comandos.md#servidores-e-instancias)
- [011 — Definiciones, defaults y puertos](../011-m1-definiciones-server/README.md)
- [Anuncio upstream: PR 26508](https://github.com/ggml-org/llama.cpp/pull/26508)

## Motivo
El 9 de octubre de 2026 el usuario probó manualmente `server start` y recibió el aviso de llama.cpp sobre el próximo cambio de puerto predeterminado a 9931. La PR enlazada anuncia esa transición; no demuestra por sí sola que todos los builds ya hayan cambiado el default. Coquena persiste y emite un puerto concreto, por lo que la alineación requiere un cambio propio, no esperar que el backend elija otro.

## Trabajo y aceptación
- [x] Actualizar la regla canónica y sus diagramas/ejemplos para usar 9931 como puerto inicial con catálogo vacío y en la creación inicial de default.
- [x] Actualizar implementación, descripción del parámetro port y pruebas pertinentes, manteniendo max+1 sobre los puertos guardados cuando hay servidores registrados.
- [x] Probar catálogo nuevo/default inicial en 9931, creación posterior por max+1, selección explícita de otro puerto, colisión y overflow sin cambios parciales.
- [x] Probar que catálogos existentes con 8080 u otros puertos conservan sus valores y que start/restart no migran ni reasignan puertos. Unset port mantiene su regla max+1 y no salta a 9931.
- [x] Mantener emisión explícita de --port según la definición: sin dependencia del default de cada versión de llama.cpp, consultas de red ni resolución dinámica de puerto al iniciar.
- [x] Ejecutar pruebas pertinentes, publicación/ejecución Native AOT win-x64, validación de documentación y git diff --check; registrar evidencia.

## Límites
Sólo el puerto inicial. Sin migración automática de servidores existentes, reasignación por puerto ocupado, cambios de host/metrics, daemon ni ampliación a M2. Registrar la tarjeta no autoriza ni completa su implementación. No bloquea las entregas 012–014.

## Evidencia
### Implementación y validación — 9 de octubre de 2026

Dependencia 011 complete; alcance y dependencias revisados con board/search/view.
Checkout inicialmente limpio; estado in-progress antes de implementar.
ServerManager usa 9931 en la inicialización y al crear con catálogo vacío;
catálogos existentes siguen usando su máximo más uno, sin migración.
Actualizados descripción de port, regla canónica, secuencia y ejemplos JSON.

La regresión de catálogo nuevo falló con el código anterior (esperaba 9931).
Suite administrada completa y suite Native AOT completa: exit 0, incluidas
72 comprobaciones de definiciones y 66 de ciclo de procesos. Verifican selección
explícita, colisiones, overflow y fallo de escritura sin pérdida del documento,
asignación concurrente, conservación de 8080 y unset a 8081 en catálogo existente.
Start/restart conservan el puerto guardado y la fixture recibe --port explícito.
Publicaciones CLI y runner win-x64 sin advertencias, con análisis estricto.

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task016-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task016-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task016-tests-native/Coquena.Tests.exe artifacts/task016-native/coquena.exe
& artifacts/task013-checks/validate-docs.ps1
git diff --check
```

Smoke adicional con COQUENA_HOME aislado en artifacts/task016-smoke-home:
CLI nativa `server list` creó default, informó 9931 y persistió ese puerto.
Validación documental: 37 diagramas PlantUML, tres ejemplos JSON y 110 enlaces
locales correctos; enlaces de esta tarjeta comprobados por separado.
Sin paquetes nuevos, consultas upstream ni cambios en ${CoquenaHome}.
Las pruebas de procesos usan backend fixture; no se hizo nueva inferencia real.
El usuario aprobó la entrega, declaró terminada la tarea y autorizó un único
commit de implementación, pruebas, documentación y evidencia el 9 de octubre
de 2026. Estado actualizado a complete mediante LeanSpec.
