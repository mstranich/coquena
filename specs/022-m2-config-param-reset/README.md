---
status: planned
created: 2026-10-10
priority: medium
tags:
- m2
depends_on:
- 018-m2-param-define
created_at: 2026-10-10T15:24:01.531947400Z
updated_at: 2026-10-10T15:24:43.424765200Z
transitions:
- status: planned
  at: 2026-10-10T15:24:43.424765200Z
---
# Restablecer el catálogo de parámetros de fábrica

## Objetivo
Implementar config param-reset y su consulta de impacto para recuperar el catálogo
suministrado por Coquena y ajustar las asignaciones afectadas con confirmación.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Restablecimiento de fábrica](../../docs/catalogo-de-parametros.md#restablecimiento-de-fábrica)
- [Informe de impacto](../../docs/catalogo-de-parametros.md#consulta-del-impacto)

## Trabajo
- [ ] Resolver el catálogo de fábrica suministrado con la versión instalada.
- [ ] Reutilizar el análisis de impacto y las garantías de persistencia de 018.
- [ ] Implementar el comando, --dry-run, confirmación y ayuda.

## Aceptación
- [ ] Restablecer definiciones iniciales editadas y eliminadas, y retirar definiciones
  añadidas, manteniendo host, port y metrics protegidos.
- [ ] Conservar asignaciones válidas y comprobar la cascada de parámetros retirados
  y valores incompatibles en varios presets y servidores.
- [ ] Verificar que los cambios de default no sobrescriben asignaciones y distinguir
  el cambio de aplicabilidad del borrado de valores.
- [ ] Guardar el informe de --dry-run mediante redirección, sin escritura de estado
  ni interacción; comparar con la operación real sobre el mismo estado.
- [ ] Comprobar confirmación, cancelación y ausencia de entrada interactiva.
- [ ] Verificar consistencia ante fallos, conservación de entidades y archivos, y
  ausencia de cambios sobre instancias activas.
- [ ] Actualizar ayuda, ejecutar pruebas pertinentes y publicar/ejecutar Native AOT
  win-x64 con evidencia reproducible.
- [ ] Validar documentación afectada y git diff --check.

## Límites y dependencia
Depende de 018 para reutilizar la gestión del catálogo, el análisis de impacto y la
cascada confirmada. No restablece otras políticas del manager ni instala o elimina
backends, modelos o companions.
