---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:14.121129700Z
updated_at: 2026-10-07T22:02:14.121129700Z
---

# Hardware y selección de backend oficial

## Objetivo
Detectar plataforma/hardware y consultar metadatos oficiales con pruebas simuladas. Resolver latest y release explícito por variante; mostrar elección automática y alternativas. No descargar ni instalar.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [ ] Probar prioridad release antes de hardware, CUDA@12 distinto de release, fallback CPU nativo y exclusión automática de unknown/incompatible. Selección explícita respeta plataforma; diagnósticos no afirman capacidades no detectadas.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
