---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '004'
- '005'
created_at: 2026-10-07T22:02:15.195656900Z
updated_at: 2026-10-07T22:02:15.195656900Z
---

# Instalación y selección del backend oficial

## Objetivo
Integrar backend remote/install/list/use con transferencia común; preparar, extraer con rutas seguras y comprobar llama-server --version antes de publicar. Primer backend propio instalado autoasigna default si procede. Concretar layout recuperable y conservar intención latest/pin.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [ ] Pruebas de archivo malicioso/incompleto, --version inválido, colisión y fallo entre movimiento e índice. Reanudar instalación sin cambiar release; no registrar parciales. No calcular ni persistir SHA-256 para backends. Sin externos ni update/upgrade.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
