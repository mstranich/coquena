---
status: draft
created: 2026-11-12
tags:
- m5
- moe
- tool
created_at: 2026-11-12T03:19:48.687952800Z
updated_at: 2026-11-13T17:02:30.951171400Z
---
# Ejecutar Ajuste fino MoE desde un preset

## Objetivo

Usar el port viable para obtener una recomendación a partir de un preset de receta,
sin reconstruir comandos manualmente. Entrega de [M5](../../docs/milestones/M5-rendimiento.md).

## Alcance

- `tool moe-cache-tune`, misma operación que usa Ajuste fino MoE en la TUI.
- Partir del preset de la combinación YAML seleccionada, publicado o no.
- Permitir backend alternativo sólo para calibración, registrado sin editar YAML.
- Resolver archivos/argumentos necesarios y usar el motor acreditado en 096.
- Mostrar preparación, avance real, errores, mediciones, predicciones y recomendación.
- Conservar evidencia de la calibración y propuesta para consultar y aplicar
  después mediante 098, en ámbito local o global explícito. Rutas y formato pendientes.
- No requiere un administrador de bancos de pruebas ni comparación histórica.
- Selección abierta: comprobar capacidades del backend pero no exigir arquitectura
  MoE confirmada; capturar fallos sin cerrar la interfaz ni modificar la receta.
- No cambiar servicios publicados como efecto de obtener la recomendación.

Reglas: [ajuste MoE](../../docs/design/benchmarking.md#selección-para-ajuste-moe).

## Dependencias

- 096: viabilidad del port con ejecución real y Native AOT; bloqueo principal.
- 097 aporta índice y clasificación a la selección, pero no condiciona el motor.
- 098 y 095 completan respectivamente guardado y presentación visual.

## Separación acordada

Los metadatos de descarga/índice/verify se conservan juntos en 097; la calibración
se separa para resolver viabilidad primero. Esto reemplaza el alcance anterior
concentrado íntegramente en esta tarjeta.

## Por concretar

- Contrato CLI, referencia de preset y configuración de calibración.
- Persistencia mínima de evidencia/propuesta, consulta y cancelación.
- Aislamiento de procesos y autorización de interferencia con el equipo en uso.

## Aceptación

- [ ] Un preset real produce recomendación y evidencia consultable.
- [ ] Datos medidos y simulados se distinguen, sin prometer optimalidad universal.
- [ ] Intento con modelo no confirmado permitido; fallo informa etapa y causa.
- [ ] Cancelación y error no afectan recetas ni procesos ajenos.
- [ ] Publicación y ejecución Windows Native AOT acreditadas.
