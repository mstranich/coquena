---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '013'
created_at: 2026-10-07T22:02:23.095115200Z
updated_at: 2026-10-07T22:02:23.095115200Z
---

# Recorrido real y entregable Native AOT M1

## Objetivo
Preparar paquete ejecutable y guía breve reproducible. Acordar backend/build y dos modelos distintos adecuados a RAM/VRAM antes de descargar gigabytes; fijar recursos usados y evidencia. Ejecutar aceptación completa de M1, no implementar funcionalidades M2.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [milestones/M1-servidor-local.md](../../docs/milestones/M1-servidor-local.md)

## Trabajo y aceptación
- [ ] Aceptar previamente [015 — resolución robusta de unidades GGUF de Hugging Face](../015-m1-resolucion-gguf-hugging-face/README.md), incluido el caso Google/Gemma con y sin cuantización explícita. Es un bloqueo de aceptación de M1, no un motivo para detener las tareas independientes 010–013. La relación se expresa aquí porque `leanspec rel` informa que todavía no está migrado al adaptador; no se editaron metadatos manualmente.
- [ ] Probar instalación, descarga/pausa/resume, dos presets con inferencia, métricas/logs, -d/stop/restart y fallos de dependencias/puerto/shards. Pruebas automáticas pequeñas más integración real opt-in con binario publicado; registrar resultados, límites y pasos para el usuario.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
