---
status: planned
created: 2026-10-07
tags:
- m1
depends_on:
- '006'
- '009'
- '011'
created_at: 2026-10-07T22:02:21.184749700Z
updated_at: 2026-10-07T22:02:21.184749700Z
---

# Inicio, parada, reinicio e identidad de procesos

## Objetivo
Integrar start foreground/-d, stop por ID y restart con plan/INI y backend concreto. Persistir instancia, capturar salida en texto y coordinar exclusión de inicio/publicación. Implementar recuperación verificando identidad del proceso, sin daemon propio.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [servidores.md](../../docs/servidores.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Proceso fixture y binario nativo prueban Ctrl+C deteniendo foreground, detached sobreviviendo terminal, segundo start sin duplicación, puerto ocupado, PID reutilizado/identidad incierta y caída. Stop limpia registro/INI y conserva log; restart usa configuración guardada y backend instalado vigente, sin red.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [ ] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Pendiente de implementación. Crear esta tarjeta no completa la entrega.
