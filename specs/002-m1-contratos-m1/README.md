---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '001'
created_at: 2026-10-07T22:02:11.164831800Z
updated_at: 2026-10-08T00:18:12.522827Z
completed_at: 2026-10-08T00:18:12.522827Z
transitions:
- status: in-progress
  at: 2026-10-08T00:15:04.973124300Z
- status: complete
  at: 2026-10-08T00:18:12.522827Z
---
# Contratos y fixtures mínimos de M1

## Objetivo
Definir datos mínimos de backend, selección remota, unidad GGUF, preset, servidor, operación e instancia. Usar IDs estables y JSON con serialización compatible con AOT; no crear un framework ni implementar M2.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [modelo-de-datos.md](../../docs/modelo-de-datos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Round-trip de fixtures pequeños; distinguir intención de selección y ejecución concreta. Revisar esquema de servidores acordado y diagnosticar versión desconocida/documento inválido.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Verificado el 7 de octubre de 2026, SDK 10.0.401, Windows x64.

### Entrega y decisiones técnicas
- Contratos pequeños en `src/Coquena/Contracts`, sin paquetes nuevos ni framework.
  IDs opacos GUID no vacíos; referencias por ID, nunca por nombre o ruta.
- Backend oficial instalado separado de selección latest/fixed; null en un servidor
  conserva herencia. Latest guarda ggml/variante; fixed guarda backendId.
  La instancia conserva backend/build concretos y recursos efectivos.
- Se respeta el JSON acordado de config/servers/instancia, camelCase e indentado;
  sólo parámetros propios en servidores y puerto concreto. Los placeholders de ID
  se sustituyen por GUID. No se persisten defaults resueltos en la definición.
- Modelo gestionado como unidad ordenada (uno o varios archivos), selección HF con
  referencia solicitada y commit fijado, configuración inicial vacía y preset por ID.
  Identidad de contenido tipada, separada de ETag/Last-Modified de transferencia.
- Operación con GUID, selección concreta exclusiva de modelo/backend, varios archivos,
  checkpoint, estado/fase y diagnóstico; backend sin digest y sin release latest.
- System.Text.Json con contexto generado y JsonTypeInfo explícito; sin reflexión.
  Lectura diagnostica `unknown-schema` e `invalid-document`: sintaxis, campos requeridos,
  duplicados, tipos/IDs inválidos, selección incoherente, puertos y servidores duplicados.
  Conserva IDs no resolubles: esta entrega no repara referencias ni consulta catálogos.
- Fixtures pequeños construidos en `tests/Coquena.Tests/ContractTests.cs`: monofichero,
  dos shards ordenados, preset, herencia/fixed/latest, instancia, operación de modelo
  pausada y backend con dos artefactos (un tamaño desconocido). No contienen pesos reales.
  Se compara JSON antes/después de lectura, incluida selección fijada y checkpoint.

### Verificaciones reproducibles

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
git diff --check
```

- Runner administrado OK; CLI y runner publicados Native AOT sin warnings/errores.
- Runner nativo: round-trips y diagnósticos de contratos OK, más los 9 casos de CLI
  internos y las 9 ejecuciones del coquena.exe publicado. Ningún comando ficticio añadido.
- Se corrigieron dos fallos detectados por pruebas: omitir null globalmente impedía
  leer argumentos de constructor requeridos; TryGetInt32 requería comprobar primero
  el tipo JSON para diagnosticar schemaVersion string sin excepción no tipada.
- JSON generado/round-trip validado por las pruebas; enlaces locales revisados.
  No se modificaron ni agregaron diagramas PlantUML.

### Límites y siguiente paso
- Son contratos/validación estructural, no funcionalidades de instalación o servidores.
  M1 continúa pendiente. No hay E/S de proveedor, persistencia ni resolución de latest.
- Modelos externos, companions, actualización y referencias de companion pertenecen a M2;
  no se implementan para anticipar esas entregas.
- Agrupación/lectura GGUF (008), seguridad de rutas/reemplazo/exclusión (003/004),
  validación de catálogo de parámetros (010), identidad verificable y coordinación de
  procesos (012), rotación y último log (013) se verifican en sus tarjetas, no aquí.
  PID de fixture no constituye identidad verificada ni prueba de proceso vivo.
- Esquemas de otros catálogos y registros de operación son mínimos técnicos de esta
  entrega; las extensiones se revisarán con evidencia en su tarea, sin migración inventada.
- Sin commits. Siguiente tarea habilitada: 003, home y persistencia local segura.
