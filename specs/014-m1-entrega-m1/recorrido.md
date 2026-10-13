# Recorrido M1 — Windows x64

Guía del recorrido verificado el 9 de octubre de 2026. Consultar el estado,
evidencia y límites en [la tarjeta 014](README.md). Los comandos de esta guía
usan la sintaxis vigente; la evidencia histórica conserva las invocaciones originales.
Requiere Windows x64, backend oficial CUDA 13 b11482, 32 GiB de RAM y, para
la configuración de prueba, NVIDIA RTX 3080 Ti de 12 GiB. No requiere .NET
para ejecutar el paquete Native AOT.

## Recursos fijados

| Recurso | Versión/commit | Archivo |
|---|---|---|
| ggml/cuda@13 | b11482 / b86d2f075 | llama-server.exe y bibliotecas oficiales |
| google/gemma-4-26B-A4B-it-qat-q4_0-gguf:Q4_0 | d1c082be9cf3c8a514acf63b8761f4b41935842e | gemma-4-26B_q4_0-it.gguf |
| prism-ml/Ternary-Bonsai-27B-gguf:Q2_0 | 86e89f34c93201c3dfd5e5880fedb0022fc7e34d | Ternary-Bonsai-27B-Q2_g64.gguf |

El paquete no incluye los pesos ni llama.cpp. Las selecciones remotas resuelven
main al ejecutar; comparar commits y archivos del menú con esta tabla antes de
aceptar. Los commits fijan la evidencia, no prometen que main nunca cambie.
Usar un home dedicado para reproducir desde cero; este recorrido existente usa
D:\Coquena. No ejecutar los pasos de creación dos veces sobre los mismos nombres.

```powershell
$env:COQUENA_HOME = 'D:\Coquena'
$coquena = '.\coquena.exe'
& $coquena system probe
& $coquena backend install cuda@13 --release b11482
& $coquena model download google/gemma-4-26B-A4B-it-qat-q4_0-gguf:Q4_0
& $coquena model download prism-ml/Ternary-Bonsai-27B-gguf:Q2_0
```

Elegir explícitamente Q2_g64 en el segundo menú. No elegir Q2_0 por posición ni
descargar companions. Ctrl+C durante transferencia pausa y muestra GUID; conservarlo
y ejecutar `coquena download resume <guid>`. `download status <guid>` informa
bytes confirmados. Una instalación completa limpia su operación.

## Configuración de prueba

Gemma excede la VRAM con todos sus pesos: la prueba usa descarga parcial de capas
a GPU, contexto corto y un único modelo cargado por el router. La idoneidad y el
rendimiento efectivos deben comprobarse con inferencia.

```powershell
& $coquena preset create google/gemma-4-26B-A4B-it-qat-q4_0-gguf:Q4_0 m1-gemma
& $coquena preset set m1-gemma ctx-size 2048
& $coquena preset set m1-gemma n-gpu-layers 20
& $coquena preset create prism-ml/Ternary-Bonsai-27B-gguf:Q2_0 m1-bonsai
& $coquena preset set m1-bonsai ctx-size 2048
& $coquena preset set m1-bonsai n-gpu-layers all
& $coquena server create m1-acceptance --port 18080
& $coquena server m1-acceptance use ggml/cuda@13:b11482
& $coquena server m1-acceptance set models-max 1
& $coquena server m1-acceptance preset add m1-gemma m1-bonsai
& $coquena server m1-acceptance show
& $coquena server m1-acceptance start -d
```

## Inferencia y consulta

Solicitudes al API de llama-server. El campo model contiene el nombre del preset.
Esperar disponibilidad del router antes de consultar; la primera petición carga pesos.

```powershell
Invoke-RestMethod 'http://127.0.0.1:18080/v1/models'
foreach ($preset in @('m1-gemma', 'm1-bonsai')) {
    $body = @{
        model = $preset
        messages = @(@{ role = 'user'; content = 'Reply with a short greeting.' })
        max_tokens = 256
        temperature = 0
    } | ConvertTo-Json -Depth 5
    Invoke-RestMethod 'http://127.0.0.1:18080/v1/chat/completions' -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 300
    & $coquena server ps
}
& $coquena server m1-acceptance logs
& $coquena server m1-acceptance logs -f
```

Ctrl+C termina sólo logs -f. Comprobar `server ps` después. Con models-max=1,
el preset descargado de memoria puede aparecer como no cargado; no se espera que
ambos tengan métricas activas simultáneamente.

```powershell
& $coquena server m1-acceptance restart
& $coquena server ps
& $coquena server m1-acceptance stop
& $coquena server m1-acceptance logs
```

Para primer plano, ejecutar `server m1-acceptance start` y Ctrl+C para detener.
Para verificar supervivencia de -d, cerrar la terminal que ejecutó start y consultar
ps desde otra. La validación automatizada pequeña prueba además referencias rotas,
backend ausente, puerto ocupado, shards faltantes e identidad incierta; no romper
los archivos instalados para reproducir esos casos.
