---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 054-m3-recuperacion
created_at: 2026-10-21T23:26:05.543274100Z
updated_at: 2026-10-21T23:26:05.543274100Z
---

# Publicar y retirar presets mediante el INI del servicio target

## Objetivo

Implementar up/down y apply/withdraw como reconciliación de presets, con identidad local y resultado remoto comprobable.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [servidores.md](../../docs/servidores.md)
- [secuencias.md](../../docs/secuencias.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver target default salvo selección explícita, self o servicio nombrado; up pregunta ante destino detenido y --start-service autoriza sólo ese inicio sin pregunta adicional, sin reiniciar uno activo ni aceptar riesgos de publicación.
- [ ] Mantener up/down y apply/withdraw interactivos, independientes del proceso desacoplado: consultar actividad sin cargas, advertir ocupación o incertidumbre en las entradas afectadas y permitir aplicar ahora o cancelar antes de publicar INI. Sin interacción disponible, rechazar sin mutación; no admitir up -d ni encolado/drenaje propios. Seguir la regla de servidores, sin crear una política no interactiva implícita.
- [ ] Agregar/actualizar con up y remover con down los presets propios en el INI operativo del target; ambos publican INI y solicitan refresh, conservando entradas ajenas.
- [ ] Down usa nombres/destino previos de .coquena/ aunque cambie carpeta o YAML; retirada confirmada permite adoptar name actual en up posterior sin dar de baja el servicio.
- [ ] Probar rename, copia de estado, cambio de alias default, colisiones y retiradas inciertas; no apropiarse de publicaciones ajenas ni liberar referencias prematuramente.
- [ ] Implementar apply/withdraw individual y atajos con selector de preset; respetar enable, independencia de entidades y warmup sólo explícito, sin gestionar residencia del backend.
- [ ] Mantener operación recuperable, revisión de INI y snapshots; no repetir mutaciones inciertas ni asumir transacción remota. Restaurar sólo entradas afectadas sin reinicio implícito.
- [ ] Probar INI de dos consumidores, target detenido/inaccesible, fallo parcial/tardío y recursos protegidos hasta confirmar resultado.

## Dependencias

- [054: Recuperar snapshots funcionales sin reinicios implícitos de consumidores](../054-m3-recuperacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin controles de modelos residentes, política propia de ocupación, borrado de pesos o reinicio para completar una retirada. No depende del dashboard ni de observación continua.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
