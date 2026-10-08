---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
created_at: 2026-10-07T22:02:13.145733200Z
updated_at: 2026-10-08T01:08:14.020741100Z
completed_at: 2026-10-08T01:08:14.020741100Z
transitions:
- status: in-progress
  at: 2026-10-08T00:55:28.525138300Z
- status: complete
  at: 2026-10-08T01:08:14.020741100Z
---
# Transferencias y comandos de descarga reanudable

## Objetivo
Implementar motor común con transporte inyectable, GUID persistido antes de bytes, preparación y progreso por archivo. Integrar download list/status/pause/resume/cancel, foreground y Ctrl+C; selección fijada recibida del consumidor, sin decidir modelo ni hardware.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Fixtures HTTP prueban Range/200/validadores, total desconocido, caída, reintentos acotados, pausa coherente, ejecutor exclusivo y reanudación sin repetir archivos completos. Cancelar limpia sólo preparación; fallo preserva GUID y devuelve prompt. Consumidores posteriores completan publicación/limpieza.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación y verificaciones realizadas el 7 de octubre de 2026. Entrega aceptada por el usuario.

### Implementación de 004
- Motor en `src/Coquena/Downloads/DownloadManager.cs`, transporte inyectable que recibe
  operación/archivo/offset y devuelve respuesta HTTP. HttpDownloadTransport resuelve
  ubicación efímera desde selección fijada; sin proveedores ni URLs persistidas.
  HTTP real por HTTPS; HTTP loopback permitido para fixtures. Redirecciones,
  descompresión automática y cookies deshabilitadas; no credenciales ambientales.
- Create valida selección y rutas antes de persistir GUID; ninguna escritura de bytes
  precede al documento. Parciales sólo bajo downloads/<guid>/files, destinos separados
  bajo models/backends; duplicados, escapes y reparse points detectados se rechazan.
- Lease de ejecutor por operación: executor.lock abierto con FileAccess.ReadWrite y
  FileShare.Delete. Un segundo escritor no puede abrirlo. Posesión comprobada por el
  handle del SO, no por PID ni existencia del archivo. Creación del lease y controles
  se coordinan mediante exclusión breve del home; nunca durante red. List/status
  recupera operaciones interrumpidas, sin pausar ejecutores vivos ni iniciar red.
- Pause/cancel externos escriben solicitudes en preparación. El ejecutor sondea cada
  50 ms, cancela transporte, espera al escritor, Flush(true) y checkpoint, luego guarda
  pausa o limpia. El controlador espera adquirir el lease (máximo 30 s cancelables)
  antes de confirmar. Ctrl+C alimenta el mismo CancellationToken de pausa conservadora.
- Flujo secuencial, buffer 64 KiB; checkpoint/progreso como máximo una vez por segundo
  durante bytes, más límites de archivo e interrupción. Cabeceras y lectura inactiva:
  30 s cada una; no hay timeout global para una transferencia larga activa.
- Máximo tres tentativas por archivo para error de red/lectura, 408/429 y
  500/502/503/504. Espera base 250 ms; Retry-After se respeta si <=30 s; espera mayor
  conserva trabajo con diagnóstico para resume explícito. No reintentar disco/persistencia
  ni errores de identidad/rango. Un fallo detiene el conjunto antes del siguiente archivo.
- Range e If-Range fuerte o fecha si disponible; comprobar inicio/final/total, longitud,
  contenido no codificado y continuidad ETag/Last-Modified. HTTP 200 nunca se concatena.
  Si no continúa, consentimiento explícito por archivo para truncar y reiniciar con el
  mismo GUID. Cambio/ausencia del validador esperado falla sin ofrecer cambio de contenido.
  Entrada redirigida o sin respuesta no implica consentimiento. 416 sin validadores no
  autoriza descartar bytes. Selección fijada es contrato del adaptador, no se re-resuelve.
- Parcial adelantado o menor que checkpoint produce checkpoint-inconsistent sin red.
  No se reconcilia por mera longitud: falta evidencia de pertenencia de esos bytes.
- Estado de archivo transferred significa sólo transporte/tamaño terminado; no es
  completed ni integridad del recurso. Se conserva y no repite ese transporte al resume.
  Operación queda verifying/resource-validation para 006/009, sin destino publicado.
  Digest, GGUF, extracción, sondeo, publicación y limpieza de éxito son del consumidor.
  El consumidor debe validar el conjunto completo, incluidos prefijos conservados.
- Cancelación elimina sólo preparación del GUID con rutas comprobadas. Se retiene
  operation.json hasta eliminar payload; si limpieza falla, conserva/restaura registro
  cancelled/cleanup con diagnóstico, y cancel permite completar. Registro JSONL tras
  limpieza añade result numérico 0 (Cancelled), sin texto libre ni secretos. Límites y
  rotación de 003 sin cambios. Caída entre estado/log/limpieza no es transacción atómica.
- CLI list/status/pause/resume/cancel, filtros excluyentes, detalle por archivo y total
  desconocido sin porcentaje. Resume foreground admite transporte inyectado; ejecutable
  aún sin proveedores informa provider-not-integrated conservando selección/parciales.
  No anuncia funcionalidad de proveedores inexistente ni agrega comandos de fixtures.
- Sin paquetes, cambios de contratos de operación, acceso a legacy ni commits.

### Verificaciones ejecutadas
```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
plantuml -checkonly "artifacts/validation/004/*.puml"
git diff --check
```

### Límites y revisión
- Proveedores reales: 006 backend; 007 metadatos y 009 modelos. No tráfico externo ni
  descarga de modelos reales. Sin companions de M2; el filtro devuelve cero de ese tipo.
- La pausa se prueba con cancelación del mismo token usado por Ctrl+C, y con controles
  desde otro proceso nativo. No se inyectó una señal física Ctrl+C del sistema operativo.
- Exclusión para procesos Coquena cooperantes en Windows local, no prueba contra cambios
  hostiles concurrentes de rutas/reparse points ni validación de volúmenes de red.
- Una caída durante limpieza puede dejar registro cleanup o directorio incompleto;
  se diagnostica, no se adivina una instalación ni se borra catálogo. Fallo de restauración
  del registro también se propaga. Estado y auditoría no son una transacción multidocumento.
- Entrega aceptada y tarjeta cerrada; no representa M1 terminado.

### Resultados
- Runner administrado: nueve casos CLI originales, contratos/persistencia y 53
  comprobaciones de descargas OK. Runner publicado Native AOT: lo anterior, nueve
  ejecuciones de coquena.exe y una ejecución adicional download status; 54 comprobaciones
  de descargas OK. Dos procesos nativos prueban exclusión, foreground, pausa y cancelación.
- HTTP simulado: rango correcto/incorrecto, 200 con y sin consentimiento, ETag cambiado
  o ausente, Range/If-Range emitidos, tamaño desconocido, corte tras prefijo y continuación,
  reintentos agotados y Retry-After excesivo, tamaño anunciado incorrecto, redirección
  rechazada. Verificados checkpoint menor/adelantado, operación interrumpida y archivos
  transferidos que no se repiten. No se confunde transporte terminado con instalación.
- Limpieza fallida inyectada manteniendo abierto un parcial: registro cleanup conservado,
  cancel posterior limpia; archivos instalados conservados. Corrupción JSON y fallo de
  persistencia son visibles, no éxito simulado. Solicitud desde otro proceso espera escritor.
- Publicaciones Native AOT de CLI/runner sin warnings ni errores en la ejecución final.
  Se corrigió IL3000 de Assembly.Location en el lanzador de fixtures: usa
  AppContext.BaseDirectory; prueba ejecutada también con runner nativo.
- Bloques PlantUML de descargas.md y comandos.md extraídos y verificados con -checkonly;
  JSON/JSONL y selección/checkpoints comprobados por pruebas. Enlaces locales de tarjeta
  verificados; diff --check limpio tras normalizar la edición de evidencia.
- El adaptador LeanSpec todavía no implementa deps; relaciones y estado revisados
  mediante board/search/view, sin usar validate ni afirmar validación nativa del adapter.

### Aceptación
El usuario confirmó que la tarea está terminada y autorizó el commit de implementación, pruebas y evidencia.
