---
status: planned
created: 2026-10-16
priority: high
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:29.755753600Z
updated_at: 2026-10-16T20:15:13.230780700Z
---

# Preservar descargas reanudables ante contención del home

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Problema y evidencia

La prueba Ornith reprodujo dos veces que retener el escritor global 8500 ms
interrumpe una transferencia con writer-busy: parcial de 65536 bytes y checkpoint
persistido de 0. Status lo clasifica como checkpoint-inconsistent y resume no puede
continuar. El control de 2000 ms conserva 131072/131072 y termina la transferencia.
No se observó desbordamiento: los offsets son long.

LocalStore.Acquire espera 3000 ms. DownloadManager escribe antes de guardar el
checkpoint; fallan tanto el guardado periódico como el finally y StorageException
escapa del tratamiento de transferencia. ModelInstaller.FinalizeAsync mantiene
store.Exclusive durante copia, flush y hashes completos de PublishFile: una sección
crítica cuya duración escala con los pesos. Esto identifica un mecanismo suficiente;
no atribuye retrospectivamente cada bloqueo de la sesión real.

## Objetivo y alcance

Corregir conjuntamente la contención prolongada de publicación y la persistencia
segura de checkpoints, para que operaciones concurrentes admitidas no obliguen a
redescargar por una contención transitoria. Diseñar la coordinación tras reproducir
el fallo con el código actual; no limitarse a aumentar el timeout o ampliar un catch
sin demostrar recuperación. Mantener exclusión de ejecutor por GUID y publicación
coherente. Si una política nueva de recuperación resulta necesaria, resolverla
explícitamente contra las reglas canónicas antes de implementarla.

No truncar, adoptar ni reutilizar bytes no confirmados sin una política aprobada y
evidencia de integridad. No introducir daemon, TUI, proveedores, migraciones ni
funciones declarativas. Revisar otros consumidores del mismo motor sólo para
preservar sus contratos y comprobar regresiones.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Descargas](../../docs/descargas.md), [modelos](../../docs/modelos-y-companions.md).
- [Evidencia e investigación Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: src/Coquena/Downloads/DownloadManager.cs,
  src/Coquena/Storage/LocalStore.cs y src/Coquena/Models/ModelInstaller.cs.
- Diagnóstico local: ${CoquenaHome}/validation/release-0.2.0/contention;
  trasladar el caso a pruebas versionadas, sin depender de esa carpeta.
- Antecedentes completos: 004, 009, 027 y 035; no son bloqueos pendientes.

## Trabajo y aceptación

- [ ] Incorporar una regresión determinista para contención superior a 3 s y su control.
- [ ] Comprobar publicación de un recurso mientras otra operación guarda checkpoints,
  sin depender de descargar pesos remotos ni del rendimiento del disco de una PC.
- [ ] Acotar las secciones críticas pesadas y asegurar un checkpoint final recuperable;
  impedir pérdida de progreso confirmado, duplicación de ejecutores o publicación parcial.
- [ ] Cubrir pausa, cancelación, caída, reanudación, integridad y publicación/limpieza
  interrumpidas bajo la coordinación elegida, incluyendo modelos y companions.
- [ ] Diagnósticos precisos y sin credenciales: distinguir contención de corrupción;
  no presentar bytes transferidos como recurso instalado.
- [ ] Ejecutar pruebas pertinentes y publicación/ejecución Native AOT win-x64,
  con homes y temporales en D:, sin alterar los recursos personales de la prueba.
- [ ] Registrar evidencia, límites y consecuencias para la preparación de recursos de M3.
  Actualizar reglas funcionales sólo si se aprueba un cambio de comportamiento.
- [ ] Presentar para revisión; no cerrar ni hacer commit/publicar sin aprobación.

## Estado de autorización

Se autoriza crear esta tarea para corrección futura. Su redacción no autoriza iniciar
implementación. El usuario autorizó después publicar 0.2.0 con este problema conocido abierto; no autorizó implementar esta corrección.
