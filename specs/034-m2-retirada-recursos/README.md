---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 023-m2-backends-externos
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:45.885192400Z
updated_at: 2026-10-10T16:48:27.134655700Z
transitions:
- status: planned
  at: 2026-10-10T16:48:27.134655700Z
---
# Retirar recursos según propiedad y uso

## Objetivo
Retirar instalaciones y registros según propiedad, uso activo y resultados reales del borrado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#quitar-un-modelo-o-companion)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar backend uninstall, model remove y companion remove; externos pierden
  registro sin tocar archivos y gestionados eliminan sólo archivos propios.
- [ ] Bloquear uso activo con diagnóstico; conservar presets/selecciones como referencias
  rotas sin reasignar automáticamente.
- [ ] Contar bytes efectivamente liberados y conservar propiedad/información para reintentar
  eliminaciones parciales sin anunciar éxito completo.
- [ ] Probar shards, companion compartido, backend default/pin, nombres reutilizados y fallos;
  conservar archivos vecinos y carpetas no vacías.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No implementa limpieza global ni retención de builds, cubierta por 025.
