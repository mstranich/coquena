---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:43.892788900Z
updated_at: 2026-10-10T16:48:26.642155600Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.642155600Z
---
# Actualizar metadatos y verificar integridad local

## Objetivo
Exponer observaciones locales e integridad como operaciones explícitas y diferenciadas.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#integridad-local)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar refresh/verify de modelos y companions; refresh anota observaciones sin red
  ni reemplazar hashes o afirmar integridad.
- [ ] Comparar SHA-256 completos por archivo/conjunto con discrepancia, referencia ausente,
  error de lectura y shards faltantes; no reparar ni rebasar referencias.
- [ ] Implementar --set-baseline sólo para externos con advertencia de alcance; lectura
  fallida o archivo cambiante no produce base exitosa.
- [ ] Persistir resultado/fecha y separar disponibilidad/seguimiento/integridad; reutilizar
  008 con fixtures pequeños.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No consulta HF, repara archivos ni sustituye referencias de integridad implícitamente.
