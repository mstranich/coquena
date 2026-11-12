---
status: draft
created: 2026-11-12
tags:
- m5
- tui
- perf
created_at: 2026-11-12T03:19:47.735278600Z
updated_at: 2026-11-12T03:19:47.735278600Z
---

# Pestaña Rendimiento en la TUI

## Objetivo

Agregar la pestaña «Rendimiento» para medir, comparar y ajustar presets y
configuraciones sobre el hardware local. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- Pestaña en la barra superior con vistas Medir, Comparar y Ajustar, que usan las
  operaciones de `perf` ([benchmarking](../../docs/design/benchmarking.md)).
- Vista previa y wireframes antes de implementar.
- Las pruebas no interfieren en silencio con un servicio en uso: se confirma antes.

## Dependencias

- [096](../096-m5-perf-medir/README.md): `perf` y los resultados guardados que esta tarjeta usa.

## Por concretar

- [ ] Ícono de la pestaña y orden respecto de las demás.
- [ ] Qué se ve mientras corre una medición larga y cómo se cancela.

## Criterios de aceptación (borrador)

- [ ] Vista previa revisada por el usuario.
- [ ] Revisión visual a 80×24; publicación Native AOT y ejecución del binario.
