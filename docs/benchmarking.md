# Benchmarking de rendimiento y consumo

## Objetivo y alcance funcional

Comparar rendimiento y consumo de recursos de modelos/presets/configuraciones
sobre hardware local. Una carga de prueba produce respuestas reales; no se predice
rendimiento por una fórmula ni se evalúa calidad semántica por contar tokens.

El recorrido básico puede ser secuencial. Concurrencia de gran escala, nginx y
varios servidores no son requisitos del procedimiento. Poder experimentar en vivo
no autoriza interferir silenciosamente con una carga productiva.

## Procedimiento conceptual

1. Identificar configuración efectiva, backend/build, modelo/companions y hardware.
2. Preparar servidor y modelo objetivo, distinguiendo carga, calentamiento y medición.
3. Enviar prompt y parámetros de generación definidos; esperar respuesta.
4. Medir duración desde cliente y extraer tokens/timings disponibles en JSON.
5. Registrar resultados, errores y telemetría por fase; resumir magnitudes distintas.
6. Repetir contra variantes comparables sin confundir cambios de carga y configuración.

Prompts y límites son entradas explícitas de la prueba. El límite solicitado no
es cantidad real producida; usar tokens reportados, no palabras/caracteres estimados.
No exigir /metrics, Prometheus o SSE para obtener tasas básicas de respuestas.
Router requiere el nombre de entrada correspondiente; servidor directo no.

Ejemplo ilustrativo de petición no streaming al endpoint /completion del oficial,
como en la prueba mínima de referencia; no es el schema de pruebas de Coquena:

```json
{
  "prompt": "Texto de prueba seleccionado",
  "n_predict": 128,
  "temperature": 0.2,
  "stream": false
}
```

En router se añade el selector admitido por el backend. El prompt puede reemplazarse;
no existe una batería secreta ni un mecanismo que deduzca throughput sin inferencia.

## Magnitudes

| Dato | Obtención y significado |
| --- | --- |
| Tokens generados | Campo de respuesta, por ejemplo timings.predicted_n o usage.completion_tokens según contrato |
| Tasa interna de generación | Tasa reportada o tokens / segundos de generación reportados |
| Tiempo extremo a extremo | Reloj monotónico del cliente desde petición hasta JSON completo |
| Throughput de la prueba | Tokens exitosos totales / tiempo total del conjunto, con criterio de errores explícito |
| RAM/VRAM temporal | Instrumentación adicional SO/proceso/GPU o fuente equivalente comprobada |

No intercambiar promedio de tasas por petición con throughput agregado.
Por ejemplo, 120 tokens en 3000 ms de generación son 40 tokens/s internos.
Para el lote, dividir la suma real de tokens exitosos por su duración total.
Tiempo cliente incluye esperas/transporte, distinto de generación interna.
Separar arranque, calentamiento, inferencia y posibles reintentos.
Con respuesta no streaming no se mide llegada del primer token desde cliente.
No deducir fases de razonamiento, entrada y salida de una única tasa.
Datos ausentes se marcan como no disponibles, no como cero medido.

Memoria global de dispositivo no es memoria del proceso; un router puede tener hijos.
Identificar origen, alcance, unidad, instante y fase de cada muestra.
La disponibilidad de RAM/VRAM depende de plataforma/backend; no prometer que
/metrics o /models/sse las proporcionan ni adoptar una dependencia incompatible con AOT.

## Comparación y resultados

Relacionar resultados con receta/resoluciones y carga exactas. Conservar versiones,
archivos identificados, argumentos efectivos, prompt, muestreo, calentamiento y
condiciones del hardware, sin credenciales. Tokens de modelos con tokenizadores
distintos no equivalen automáticamente al mismo trabajo o calidad.

Guardar resultados parciales de pruebas terminadas e informar progreso.
Errores/reintentos y tiempo de espera forman parte del resultado; no elegir «mejor»
solo por throughput si falla parte de la carga. Ausencia de datos, fallo de preparación
y medición real cero no son lo mismo.

El formato de pruebas/resultados, repeticiones, política de cache/seed, criterios de
ranking y duración requieren diseño. No se fija CSV ni un framework de tests por usar
un repositorio como referencia.

## Referencia técnica

[llama-throughput-lab](https://github.com/alexziskind1/llama-throughput-lab),
commit 60bf3e88170ca97c4f4a0dd2e57e64ea200e5599, se usa como referencia de procedimiento,
no dependencia de ejecución Python. Puntos de entrada:

- [Prueba mínima](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/tests/test_llama_server_single.py#L13): petición y resultado.
- [Tokens](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/tests/llama_server_test_utils.py#L425) y
  [tasa](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/tests/llama_server_test_utils.py#L440).
- [Lote](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/scripts/full_sweep.py#L101): tiempo y suma de tokens.
- [Persistencia incremental](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/scripts/full_sweep.py#L240).

No copiar el ranking con errores, descarte de stdout/stderr o arquitectura de varias
instancias como política de Coquena. El procedimiento propio se implementa en C#.
Una calculadora predictiva de recursos es una posibilidad distinta; no confundirla
con medir consumo de ejecución.
