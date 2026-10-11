---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 017-configuracion-retencion-logs
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:45.595954800Z
updated_at: 2026-10-10T16:48:27.055578400Z
transitions:
- status: planned
  at: 2026-10-10T16:48:27.055578400Z
---
# Clonar presets y resolver referencias a companions

## Objetivo
Reutilizar ajustes mediante clones independientes y resolver referencias explícitas a unidades
auxiliares.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#presets)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar preset clone a nombre libre conservando modelo o usando --model; copiar
  valores/referencias sin archivos ni herencia y probar independencia.
- [ ] Guardar --ref por ID y distinguirlo de literal; resolver rutas registradas al generar
  INI por instancia, incluidos shards y reubicaciones.
- [ ] Comprobar referencias/disponibilidad antes de iniciar; conservar presets no servibles
  con diagnóstico y rechazar inicio.
- [ ] Probar companion compartido y referencias estables ante renombres sin duplicar propiedad
  ni certificar inferencia.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No descarga ni asocia companions automáticamente; asignación básica corresponde a 017.
