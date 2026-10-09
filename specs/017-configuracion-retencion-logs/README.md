---
status: draft
created: 2026-10-09
priority: medium
created_at: 2026-10-09T14:07:09.965469Z
updated_at: 2026-10-09T14:07:24.090154500Z
transitions:
- status: draft
  at: 2026-10-09T14:07:24.090154500Z
---
# Configuración de retención de logs

## Objetivo
Diseñar la superficie CLI para consultar y ajustar la retención de logs de servidores.
Draft solicitado por el usuario durante 013; no autoriza implementación.

## Referencias
- [Servidores](../../docs/servidores.md#consulta-de-logs)
- [CLI](../../docs/comandos.md)
- [013](../013-m1-metricas-logs/README.md)

## Base aceptada
100 MiB por servidor. Limpieza al iniciar, detener o consultar logs; eliminar primero
los antiguos y proteger el activo y el último. Si los protegidos exceden el límite,
conservarlos e informar. En 013 se configura mediante config.json; no se inventan comandos.

## Decisiones por resolver
- Sintaxis CLI para consultar, modificar y restablecer la política global.
- Unidades, rangos, presentación del valor efectivo y validación de argumentos.
- Decidir si se necesita override por servidor; no asumirlo.
- Momento de aplicar cambios y diagnóstico de archivos protegidos.

## Trabajo futuro
- [ ] Resolver decisiones y actualizar documentación canónica/ayuda.
- [ ] Implementar sólo la interfaz aprobada con persistencia segura.
- [ ] Probar gramática, valores inválidos, compatibilidad y protección de logs.
- [ ] Publicar y ejecutar Native AOT win-x64 y registrar evidencia.

## Límites
No cambiar el ciclo de procesos, añadir TUI ni ampliar retención a otros recursos.
Sin dependencia registrada mientras la tarjeta permanezca en diseño; 013 aporta la base.
