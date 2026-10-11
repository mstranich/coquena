---
status: planned
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:43.597969200Z
updated_at: 2026-10-10T16:48:26.555552600Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.555552600Z
---
# Descargar y catalogar companions gestionados

## Objetivo
Instalar unidades auxiliares de forma independiente del modelo principal y de los presets.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#companions)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar companion download/list reutilizando selección de 015 y publicación de 009,
  con tipo y cuantización independientes.
- [ ] Probar menú, README a pedido, cancelación antes de GUID y ausencia de candidatos en
  commit fijo.
- [ ] Descargar sólo unidad auxiliar/partes y publicar conjunto completo en ubicación
  configurada; probar pausa/reanudación/fallos/colisiones con otras unidades.
- [ ] No descargar principal, asociar presets automáticamente ni certificar compatibilidad de
  inferencia.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No descarga principales ni asocia presets automáticamente. Registro externo, actualización,
retirada y referencias tienen entregas propias.
