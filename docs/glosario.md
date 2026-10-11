# Glosario

| Término | Significado |
|---|---|
| **Distribución** | Identificada por el repositorio de origen; `ggml` nombra `https://github.com/ggml-org/llama.cpp` y `oficial` es su alias. Junto al release permite asociar parámetros. Sólo el oficial es fuente de descargas; declarar un tercero no lo convierte en fuente de confianza. |
| **Familia de backends** | Tipo de aceleración y, cuando corresponde, versión de sus bibliotecas: `cuda@12`, `cuda@13`, `vulkan`, `cpu`. En `cuda@12`, `12` no es el release de llama.cpp. |
| **Build de llama.cpp** | Release del binario oficial, p. ej. `b6123`. Una familia puede tener varios builds; un mismo build puede publicarse para varias familias. |
| **Backend registrado** | Binario concreto con ID único y estable, familia y versión sondeada. Los externos pueden no seguir la numeración de builds oficiales. |
| **Aceleración** | Para qué se compiló el binario: `cpu`, `vulkan`, `cuda` (con major 12 o 13), `hip`, `sycl`, `openvino`… Depende del sistema operativo y la arquitectura. |
| **Dispositivo** | Una unidad de cómputo de la máquina: la CPU o una GPU concreta (NVIDIA, AMD, Intel). |
| **Nombre canónico** | `usuario/repositorio:cuantización` para el principal; `usuario/repositorio#tipo:cuantización` para un companion. Sin aliases; comparación local case insensitive. En externos es declarado, no procedencia certificada. |
| **Modelo** | Unidad principal: un GGUF o un conjunto esperado de shards. Un externo puede registrarse incompleto con uso bloqueado; una instalación gestionada sólo se publica completa. |
| **Directorio de modelos** | Área configurable donde Coquena guarda modelos y companions gestionados. |
| **Externo** | Archivo o conjunto registrado por Coquena pero gestionado por otra herramienta o por el usuario. Coquena no lo mueve ni lo borra al quitar su registro. Aplica a modelos, companions y backends. |
| **Companion** | Unidad registrada para un papel auxiliar, sin dependencia de otro registro para estar disponible: proyector multimodal (`mmproj`), modelo borrador para decodificación especulativa, etc. |
| **Servidor** | Definición persistida de presets explícitos, backend propio o heredado del default y parámetros del router. No es una instancia. |
| **Archivo INI** | El `--models-preset` que recibe `llama-server`. Coquena lo **genera** en cada lanzamiento a partir de su modelo interno; vive en el home de Coquena mientras dura la instancia y se borra al detenerla. |
| **Preset** | Un comportamiento concreto de un modelo (nombre visible al cliente, companions, parámetros). Guarda sus propios valores, sin herencia global ni del modelo. Se puede clonar a otro modelo. |
| **Instancia** | Un lanzamiento identificado por un ID propio: `llama-server`, servidor, backend concreto, puerto, PID, INI generado y logs. Cada servidor tiene como máximo una instancia activa; distintos servidores pueden ejecutarse simultáneamente. |
| **Primer plano / daemon** | Los dos modos de lanzar una instancia, como en Docker: adjunto a la terminal siguiendo logs, o desacoplado en segundo plano. |
| **Home** | Directorio propio de Coquena, resuelto por las reglas de [comandos.md](comandos.md#home), incluida la prioridad de COQUENA_HOME. Los directorios de modelos y backends se configuran separadamente. |
| **Parámetro** | Definición del catálogo con tipo, default, opciones y aplicabilidad por binario/versión. Su valor se asigna a un preset o servidor según su ámbito. |
