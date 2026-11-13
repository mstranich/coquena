# M5 — Herramientas para ajustar y compartir recetas

**Estado:** definición en borrador. **Depende de:** [M4](M4-experiencia-completa.md).

## Objetivo y cierre

Facilitar que un preset existente obtenga una recomendación de caché MoE para el
equipo local, que el usuario pueda revisarla y guardarla en la receta o un override.
Chat permite una prueba rápida de respuesta y telemetría con pocos prompts.
No se pretende construir una plataforma de benchmarking ni reemplazar un arnés.

El recorrido de entrega es: seleccionar preset, calibrar, consultar mediciones y
predicciones, confirmar una propuesta y conservarla en YAML. La receta resultante
es distribuible; su optimalidad en otro hardware no se presume. La prueba con el
agente del usuario no se convierte en una batería automatizada obligatoria.

## Primera prioridad: viabilidad

Antes de desarrollar la integración completa, 096 debe acreditar el port C# de
`scripts/moe-cache-tune.py` con una calibración real y Windows Native AOT. Lectura
estática, ayuda de ejecutables o compilación no bastan. Si hay impedimentos, se
revisa el alcance con esa evidencia antes de continuar el trabajo que depende del port.
La viabilidad no depende de tener TUI, índice MoE o edición de recetas terminados.

## Entregas

| Tarjeta | Responsabilidad | Relación |
| --- | --- | --- |
| [096](../../specs/096-m5-viabilidad-moe-cache-tune/README.md) | Viabilidad del port MoE: trazas, simulación, ajuste y procesos Windows | Primera prioridad; bloquea integración MoE |
| [097](../../specs/097-m5-metadatos-modelos/README.md) | Metadatos de arquitectura/expertos en descarga, índice y verify | Soporte de selección; no bloquea el experimento de viabilidad |
| [099](../../specs/099-m5-moe-cache-tune/README.md) | `tool moe-cache-tune` desde preset, con recomendación y evidencia mínima | Usa port viable de 096 |
| [098](../../specs/098-m5-aplicar-ajuste-receta/README.md) | Aplicar propuesta al preset y guardar en receta/override | Operación separada; usa recomendación de 099 |
| [100](../../specs/100-m5-chat-prueba/README.md) | Chat rápido, telemetría y acceso desde En vivo | Puede avanzar sin esperar al port |
| [095](../../specs/095-m5-tui-herramientas/README.md) | Herramientas con Chat y Ajuste fino MoE | Integra operaciones; no duplica sus implementaciones |

Todas permanecen draft. La numeración no expresa orden de ejecución. Se reutilizan
y reformulan los borradores existentes, retirando sus contenidos de Bench, carga
concurrente y comparación general. No representan funciones entregadas.

## Límites del milestone

- Sin llama-bench, batched-bench, catálogo/formulario Bench ni migración de server-bench.
- Sin integración de throughput-lab, barridos HTTP o réplicas balanceadas; nginx no
  forma parte del mapa de producto ni se reemplaza por un balanceador propio.
- Sin administrador general de bancos de pruebas, comparador histórico o ranking.
- Conservar evidencia mínima de calibración y recomendación no exige esas funciones.
- Sin historial persistente de Chat, herramientas de agente o evaluador de calidad.
- Descarga, índice y verify permanecen juntos como entrega de metadatos.

## Decisiones funcionales conservadas

Herramientas ofrece Chat y Ajuste fino MoE; su descripción es «Calibrar la caché
de expertos y recomendar parámetros para el preset». CLI usa `tool`.
`tool chat --preset <service>/<preset>` abre sesión interactiva con `/exit` o Ctrl+C
y telemetría bajo la entrada. El botón Chat de En vivo lleva al mismo preset.

Ajuste fino MoE parte de la combinación YAML; Chat usa una entrada publicada.
Un backend alternativo de calibración sólo afecta al experimento. Guardar una
propuesta es distinto de publicar/reiniciar. La selección no exige clasificación
MoE confirmada: prioridad por evidencia y posibilidad de intentar otros presets.

Reglas: [ajuste y prueba rápida](../design/benchmarking.md),
[metadatos](../design/modelos-y-companions.md#evidencia-de-arquitectura-y-expertos),
[guardado](../design/interfaces-visuales.md#borradores-y-guardado).

## Incertidumbres que necesitan resolución

- Compatibilidad real de trazas/logs, simulación y cierre de procesos Windows.
- Preparación de calibración, límites de memoria, cancelación e interferencia.
- Formato/ruta mínima de evidencia y sintaxis de aplicación de propuesta.
- Presentación de Chat con alcance correcto de caché y especulación.
- Reconciliar nombres/contratos YAML con el cierre de M4 antes de implementar.

El [estudio de referencias](M5-estudio-rendimiento.md) conserva lo investigado;
las herramientas descartadas no vuelven al alcance por aparecer allí.
