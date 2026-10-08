---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '004'
- '007'
- '008'
created_at: 2026-10-07T22:02:18.126842Z
updated_at: 2026-10-07T22:02:18.126842Z
---

# Descarga y publicación de modelos gestionados

## Objetivo
Integrar model download y listado local con selección fijada, transferencia y verificación. Publicar sólo unidad completa en models/usuario/repositorio/ruta-remota y registrar ID/nombre canónico case insensitive. Sin carpeta propia por cuantización ni companion implícito.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [descargas.md](../../docs/descargas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)

## Trabajo y aceptación
- [ ] Pruebas de múltiples shards, colisiones, rutas inseguras, espacio insuficiente y recuperación entre movimiento e índice. SHA-256 completo por archivo; contraste remoto sólo comparable. Ctrl+C/resume conserva commit y partes verificadas; éxito limpia operación y registra transacción. Sin externos/update/upgrade.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
