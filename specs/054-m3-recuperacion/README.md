---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 053-m3-servicios
created_at: 2026-10-21T23:26:05.469823600Z
updated_at: 2026-10-21T23:26:05.469823600Z
---

# Recuperar snapshots funcionales sin reinicios implícitos de consumidores

## Objetivo

Conservar disponibilidad ante fallos recuperables de la candidata conforme al modo de ejecución.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md#recuperación-y-fallos](../../docs/servidores.md#recuperación-y-fallos)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Aplicar recuperación desacoplada en la misma invocación, incluida preparación fallida, y preferir última buena si la candidata ya es conocida como fallida.
- [ ] Preparar recursos exactos faltantes del snapshot sin resolver latest, recorrer históricos anteriores ni reintentar indefinidamente.
- [ ] Ofrecer decisiones en modo interactivo y diagnóstico finito cuando no haya recuperación posible, sin bloquear la recuperación ya completada por una UI.
- [ ] Mantener integridad inválida conocida como fallo fatal en ambos modos, preservando la instancia activa y sin eludirlo mediante fallback.
- [ ] Respetar service.recovery.allow-draft-last-good y separar éxito local de borrador, preparación o éxito importado.
- [ ] Probar doble fallo, recursos ausentes, YAML distinto del snapshot y conservación de contribuciones ajenas; la recuperación de una publicación no autoriza reiniciar el servicio compartido.

## Dependencias

- [053: Registrar servicios y operar su proceso con identidad segura](../053-m3-servicios/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor permanente, editor de borradores o alertas no configuradas. Las notificaciones se integran en 061.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
