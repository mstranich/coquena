---
status: archived
created: 2026-11-17
priority: medium
created_at: 2026-11-17T23:59:54.456602700Z
updated_at: 2026-11-18T03:11:37.780029200Z
completed_at: 2026-11-18T02:50:45.336885800Z
transitions:
- status: in-progress
  at: 2026-11-18T00:40:18.098942800Z
- status: complete
  at: 2026-11-18T02:50:45.336885800Z
- status: archived
  at: 2026-11-18T03:11:37.780029200Z
---
# Intervalo de observación configurable

## Objetivo

Elegir cada cuánto se leen las métricas de un servicio (`/slots`, `/metrics`,
proceso y GPU), para ganar detalle cuando hace falta o reducir la carga sobre el
servicio. Hoy los observadores usan intervalos fijos.

## Alcance propuesto

- Preferencia global `config observer-interval show|set <segundos>|unset`, junto a
  `observer-retry`, también editable en Configuración › Coquena.
- Opción por ejecución `--interval <segundos>` en `ps -w` y `service logs -f`, que
  reemplaza la preferencia sólo en esa ejecución.
- Valores con decimales, entre 0,25 y 60 s; fuera de rango se rechazan con su motivo.
- Sin preferencia ni opción, cada observador conserva su valor actual.

## Decisiones

- Nombres: `observer-interval` junto a `observer-retry`; no hay intervalo propio de
  gráficas ni de hardware.
- La preferencia aplica a `ps -w`, a la línea de `logs -f` y a En vivo. Sin valor
  propio cada uno conserva su ritmo (2 s y 0,5 s).
- `--interval` es una opción sólo de la CLI (`ps -w`, `logs -f`).
- En vivo: cada columna de las gráficas corresponde a un intervalo; la columna se
  calcula al leer, de modo que guardar otro valor en Configuración › Coquena reagrupa
  la historia sin reiniciar la TUI.

## Plan

- [x] Preferencia en la configuración, CLI y Configuración › Coquena.
- [x] Opción `--interval` en `ps -w` y `service logs -f`.
- [x] Textos en en-US y es-AR, ayuda y `comandos.md`.
- [x] Pruebas de validación, precedencia y persistencia.
- [x] Publicación AOT y ejecución contra un servicio real.

## Verificación

| Prueba | Resultado |
| --- | --- |
| `ObserverIntervalTests` y suite completa (Release) | OK |
| AOT win-x64: `config observer-interval show/set/unset`, rechazo de 0,1 y 99, `observer-retry` conserva el intervalo | OK |
| AOT: `ps -w --interval 0.5` redibuja; `ps --interval` sin `-w` y `logs --interval` sin `-f` se rechazan | OK |
| AOT contra un servicio real: `logs -f --interval`, En vivo tras guardar el intervalo | Aprobado por el usuario, sin ejecución registrada aquí |
