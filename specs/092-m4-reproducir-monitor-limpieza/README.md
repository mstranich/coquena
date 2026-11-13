---
status: draft
created: 2026-11-11
priority: medium
tags:
- m4
created_at: 2026-11-11T13:06:39.637477700Z
updated_at: 2026-11-13T02:29:32.594753200Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.726586700Z
---

# Resolver la coordinación entre monitor de descargas y limpieza

## Objetivo

Encontrar e implementar una solución a la carrera entre el monitor interno de
controles de descarga y la retirada del área temporal de la operación, preservando
el resultado de la publicación y los diagnósticos reales.
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

El [hallazgo 002](../../docs/findings/002-coordinacion-monitor-limpieza.md)
documenta una carrera reproducida en código anterior a 054: el finalizador retira
el directorio temporal mientras el monitor todavía consulta los controles de
pausa/cancelación. La excepción del monitor puede convertir una finalización
exitosa en error u ocultar otro diagnóstico. El síntoma se observó en Windows;
no se ha establecido que sea un defecto del sistema operativo ni se observó
pérdida de recursos publicados.

Debe revisarse cómo quedó esa coordinación tras los cambios posteriores. La
reproducción sirve para diagnosticar y validar la solución, no constituye por sí
sola la entrega. No reproducir el síntoma en ejecuciones ordinarias no basta para
considerarlo resuelto.

## Alcance

- Analizar el ciclo actual del monitor, el finalizador y la limpieza, usando el
  escenario original como referencia y ámbitos aislados para la evidencia.
- Determinar la causa vigente y resolver la coordinación para impedir lecturas
  del monitor sobre el área temporal una vez retirada. Evaluar la barrera de
  parada propuesta en el hallazgo sin fijarla como implementación obligatoria.
- Preservar los controles de pausa/cancelación durante las fases que los admiten,
  la exclusión del ejecutor y los diagnósticos de transferencia/publicación.
- Cubrir finalización normal, cancelación y reanudación en fase de limpieza.
- Incorporar una regresión que controle el intercalado conflictivo y validar en
  administrado y mediante publicación y ejecución Native AOT win-x64.
- Si el código actual ya contiene la solución, identificar el mecanismo que
  impide la carrera y acreditarlo con esa validación; no modificarlo sin necesidad.

La limpieza tratada es la del área temporal de una descarga, no el comando
`clean` de instalaciones. No incluye rediseñar la TUI ni cambiar políticas de
propiedad, publicación o limpieza de recursos. Aplican los contratos de
[descargas](../../docs/design/descargas.md) y
[persistencia](../../docs/design/persistencia-proyectos.md).

## Criterios de aceptación

- [ ] Causa y solución documentadas sobre el código actual, con evidencia del
  orden entre el cese de lecturas del monitor y la retirada del área temporal.
- [ ] Una publicación exitosa conserva su resultado sin errores espurios del
  monitor; los errores reales mantienen su diagnóstico.
- [ ] Finalización normal, cancelación y reanudación de limpieza verificadas,
  conservando los controles y la exclusión correspondientes.
- [ ] Regresión automatizada con intercalado controlado en ámbitos aislados,
  sin depender únicamente de repetir ejecuciones o esperas probabilísticas.
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 registradas.
- [ ] No se ocultan indiscriminadamente errores de E/S, seguridad o permisos,
  ni se afectan recursos publicados, vecinos o externos.