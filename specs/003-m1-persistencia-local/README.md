---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:12.133402100Z
updated_at: 2026-10-08T00:47:02.775724900Z
completed_at: 2026-10-08T00:47:02.775724900Z
transitions:
- status: in-progress
  at: 2026-10-08T00:40:47.947106600Z
- status: complete
  at: 2026-10-08T00:47:02.775724900Z
---
# Home y persistencia local segura

## Objetivo
Resolver home y estructura acordada; guardar catálogos JSON indentados con reemplazo seguro y exclusión breve de escritores. Configuración separada, JSONL de transacciones con límites y sin secretos. Proveer almacenamiento de operaciones e instancias sin implementar sus ciclos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Probar precedencia de home, dos escritores, JSON dañado y fallo antes/después del reemplazo. No perder cambios ni presentar documentos corruptos como catálogos vacíos. Documentar mecanismo y límites elegidos.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Verificado el 7 de octubre de 2026, Windows x64, SDK 10.0.401.

### Entrega y decisiones técnicas
- Home según la aclaración del usuario y la regla corregida en comandos.md:
  COQUENA_HOME absoluto o USERPROFILE/.coquena. Sin autodetección portable,
  sin escribir variables persistentes, sin init ni creación por mostrar ayuda.
- Home crea las áreas acordadas al escribir. Rutas relativas sin escapes, ADS,
  nombres de dispositivo o componentes ambiguos; rechaza reparse points detectados.
- LocalStore ofrece lectura y modificación local de documentos con JsonTypeInfo
  generado y validación explícita. Config separado, catálogos completos, operación
  bajo downloads/<guid>/operation.json y files/, instancia run/<servidor>/instance.json.
  El GUID de operación debe coincidir con el directorio. No implementa sus ciclos.
- Envoltorios mínimos schemaVersion/models, backends y presets, sin cambiar campos
  de los registros de 002. Validan versión, registros, IDs y nombres duplicados.
  parameters.json tiene ubicación reservada; su esquema/validación funcional
  pertenece a 010. No se inventan entradas ni un catálogo de parámetros disponible.
- Escritor único por home: .writer.lock abierto FileShare.None desde lectura hasta
  reemplazo y auditoría. Espera cancelable cada 25 ms, límite de 3 s con writer-busy.
  El archivo permanece; la posesión es el handle del sistema, no su existencia.
  El callback de modificación debe ser local y breve; no incluir transferencias/red.
- Temporal único en el directorio del destino, Flush(true), File.Replace para estado
  existente y File.Move sin overwrite en primera creación. Se compara el original
  antes de publicar para detectar cambios manuales observables. Error previo conserva
  original; error posterior informa Published=true, sin simular rollback ni reintentar
  automáticamente una modificación que ya se publicó. No hay transacción multidocumento.
- Documentos limitados a 16 MiB y UTF-8 estricto. Ausente devuelve null; JSON dañado,
  versión desconocida, permisos y otros errores no se convierten en catálogo vacío.
- JSONL: logs/transactions.jsonl, un archivo de 256 KiB y un respaldo
  transactions.1.jsonl de hasta 256 KiB. Máximo retenido 512 KiB. Eventos sólo con
  fecha UTC, GUID de transacción, tipo numérico de documento y GUID de operación
  opcional; sin texto libre, rutas, nombres, credenciales ni URLs. No es event sourcing.
  Rotación bajo la misma exclusión; un JSONL parcial/inválido se diagnostica sin
  concatenar otro evento. Fallo de auditoría no revierte estado publicado.
- Sin paquetes, nuevos comandos CLI ni commits.

### Comandos ejecutados y resultados
Desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
plantuml -checkonly "artifacts/validation/003/*.puml"
git diff --check
```

- Runner administrado: 9 casos CLI, contratos y pruebas de persistencia OK.
- Ambos ejecutables publicados como Native AOT win-x64 sin warnings ni errores.
  Runner nativo ejecuta las mismas pruebas de almacenamiento (incluidos dos procesos
  nativos escritores) y 9 ejecuciones de la CLI publicada. No se reduce la evidencia
  de esta tarea a la ayuda: el almacenamiento se ejecuta dentro del runner nativo.
- Verificados: home explícito/default/relativo/inválido, creación diferida, nombres
  peligrosos y escapes; dos tareas y dos procesos con incrementos sin pérdida;
  timeout de escritor y cancelación; configuración y catálogo corruptos conservados;
  versión desconocida y UTF-8 inválido; fallo inyectado antes/después del reemplazo;
  cambio manual observado, fallo de auditoría y JSONL truncado; rotación limitada,
  JSON indentado, almacenamiento de catálogos/operación/instancia y GUID incongruente.
- Todos los bloques PlantUML de comandos.md extraídos a artifacts/validation/003
  y comprobados con -checkonly. JSON/JSONL generado comprobado por las pruebas.
  Enlaces locales revisados y git diff --check sin incidencias.

### Límites y siguiente paso
- Infraestructura local, todavía no comandos funcionales de gestión ni M1 completo.
  004 implementará transferencia/pausa/reanudación/limpieza de operaciones;
  012 identidad, exclusión del inicio y limpieza de instancias; 013 logs del backend.
  No se crean defaults funcionales de servidor/backend por escribir un documento.
- La auditoría y el estado no constituyen una transacción: caída entre ambos puede
  dejar estado válido sin evento. Un temporal huérfano se ignora, sin recuperación
  destructiva automática. No se garantiza durabilidad frente a corte de energía.
- Exclusión para escritores Coquena cooperantes en Windows local; edición manual
  debe hacerse con Coquena detenido. No se garantiza exclusión frente a programas
  que ignoren el lease ni cambios hostiles de rutas/reparse points entre comprobaciones.
  Pruebas de rutas no equivalen a una prueba de ataque concurrente. No se validaron
  volúmenes de red ni sistemas de archivos distintos del recorrido local.
- Registros terminales de descarga no se limpian aquí automáticamente: depende de
  completar su ciclo en 004. No se retiene un historial de operaciones en otro catálogo.
- La configuración mantiene las políticas ya acordadas; el límite técnico de JSONL
  es fijo en esta entrega. No se adelanta configuración de logs de llama-server.
- Siguiente tarea habilitada: 004, transferencias reanudables. Sin commits.

### Aceptación
El usuario confirmó la entrega como correcta y terminada y autorizó el commit.
