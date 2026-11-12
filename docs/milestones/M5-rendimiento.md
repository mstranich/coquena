# M5 — Rendimiento

**Estado:** intención, con tarjetas en borrador. **Depende de:** [M4](M4-experiencia-completa.md).

## Objetivo

Dar herramientas para exigirle al hardware local y encontrar la configuración
que mejor rinde: medir, comparar y ajustar presets sobre el equipo real, y
probar un preset publicado con un chat. Las reglas de medición están en
[benchmarking](../design/benchmarking.md).

M5 es el último milestone de funciones nuevas. Después, el trabajo se dedica a
corregir, estabilizar y mejorar la experiencia a partir del uso diario.

## Superficies

- Pestaña «Rendimiento» en la TUI, con vistas para medir, comparar y ajustar.
- Subcomando `perf` en la CLI, con las mismas operaciones que la pestaña: lo que se
  hace en la TUI también se puede hacer desde la CLI, con salida de texto.

## Fuentes a incorporar

Se llevan a C# o se invocan como binarios del backend; no se depende de Python.

| Fuente | Uso previsto |
| --- | --- |
| `llama-bench`, `llama-batched-bench` (llama.cpp) | Medición sin servidor, invocada desde el backend instalado |
| `scripts/server-bench.py` (llama.cpp) | Carga contra `llama-server`, llevada a C# |
| `scripts/compare-llama-bench.py` (llama.cpp) | Comparación de corridas, llevada a C# |
| `scripts/moe-cache-tune.py` (llama.cpp) | Ajuste de la caché MoE, llevado a C# |
| llama-throughput-lab | Barridos de concurrencia, round-robin e hilos, sin su menú ni nginx |

## Tarjetas

| Tarjeta | Entrega |
| --- | --- |
| [095](../../specs/095-m5-tui-pestana-rendimiento/README.md) | Pestaña Rendimiento en la TUI |
| [096](../../specs/096-m5-perf-medir/README.md) | Subcomando `perf` y medición con `llama-bench` |
| [097](../../specs/097-m5-perf-carga-servidor/README.md) | Carga del servidor publicado |
| [098](../../specs/098-m5-perf-comparar/README.md) | Comparación de corridas |
| [099](../../specs/099-m5-perf-ajuste-cache-moe/README.md) | Ajuste de la caché MoE con escritura confirmada en la receta |
| [100](../../specs/100-m5-chat-prueba/README.md) | Chat de prueba contra un preset publicado |

## Riesgos

- Algunas herramientas dependen de detalles internos de llama.cpp (trazas, formato
  de logs) que pueden cambiar entre versiones; cada una declara la versión mínima
  del backend que admite.
- Medir sobre un servicio en uso puede afectar a quien lo está usando; las pruebas
  piden confirmación antes de interferir.
