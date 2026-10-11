---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 017-configuracion-retencion-logs
- 018-m2-param-define
- 019-m2-config-backends-dir
- 020-m2-config-models-dir
- 021-m2-system-probe
- 022-m2-config-param-reset
- 023-m2-backends-externos
- 024-m2-backend-update
- 025-m2-backend-upgrade
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
- 028-m2-refresh-integridad-local
- 029-m2-emparejamiento-externos
- 030-m2-model-update
- 031-m2-model-upgrade
- 032-m2-transicion-externo-gestionado
- 033-m2-presets-clone-companions
- 034-m2-retirada-recursos
created_at: 2026-10-10T16:36:46.214181900Z
updated_at: 2026-10-10T16:48:27.259073700Z
---

# Validar y preparar el entregable integrado M2

## Objetivo
Preparar el paquete M2 y comprobar cobertura funcional, regresión e integración del recorrido
acordado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Auditar cada comando acordado contra tarjetas/ejecutable; asignar huecos a su entrega
  sin implementar funcionalidades en esta tarjeta.
- [ ] Ejecutar regresión M1 y escenarios integrados de
  externos/companions/presets/actualizaciones/retirada/reset/directorios relativos.
- [ ] Verificar identidad y recuperación de publicaciones/traslados entre volúmenes y
  protección de externos/recursos en uso.
- [ ] Preparar paquete Native AOT win-x64 y ejecutar binario extraído; acordar recursos reales
  antes de descargar pesos y documentar resultados/limitaciones.
- [ ] Revisar ayuda, documentos y evidencia de todas las entregas; aceptación final requiere
  aprobación explícita del usuario.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No implementa funcionalidades faltantes: se asignan a su entrega. No incorpora TUI,
HTTP propio, autenticación HF, fuentes de terceros o multiplataforma. No crea pipelines
ni publica releases sin autorización específica.
