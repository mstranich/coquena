# Glosario

| Término | Significado |
|---|---|
| **Distribución** | Quién publica binarios de llama.cpp o compatibles: la oficial (`ggml-org`) u otra que un desarrollador o laboratorio incorpore a Coquena, como SDKMan admite el JDK de Oracle junto a otros. |
| **Familia de backends** | Una línea de binarios intercambiables entre sí: misma distribución, misma aceleración. P. ej. `cuda12`, `cuda13`, `vulkan`, `cpu`. |
| **Versión de backend** | Un build concreto de una familia (`b6123`) instalado por Coquena. Conviven varias versiones de la misma familia. |
| **Aceleración** | Para qué se compiló el binario: `cpu`, `vulkan`, `cuda` (con major 12 o 13), `hip`, `sycl`, `openvino`… Depende del sistema operativo y la arquitectura. |
| **Dispositivo** | Una unidad de cómputo de la máquina: la CPU o una GPU concreta (NVIDIA, AMD, Intel). |
| **Modelo** | Un GGUF principal (o un conjunto de shards) registrado en Coquena. Nunca se copia. |
| **Directorio de modelos** | La carpeta definida por el usuario donde Coquena deja lo que descarga. |
| **Modelo externo** | Un modelo registrado desde fuera del directorio de modelos; Coquena guarda su ruta y no lo mueve. |
| **Companion** | Un GGUF que acompaña a un modelo sin funcionar solo: proyector multimodal (`mmproj`), modelo borrador para decodificación especulativa, etc. |
| **Configuración global** | Valores de `llama-server` aplicables a cualquier backend. Raíz del eje backend. |
| **Configuración de familia** | Valores específicos de una familia; hereda de la global. |
| **Configuración de modelo** | Valores iniciales de un modelo; raíz del eje modelo. |
| **Servidor** | Composición que Coquena coordina: los presets que se ofrecen juntos, un puerto y, opcionalmente, una familia fija. Reemplaza al script lanzador. (Nombre provisorio.) |
| **Archivo INI** | El `--models-preset` que recibe `llama-server`. Coquena lo **genera** en cada lanzamiento a partir de su modelo interno ; vive en el home de Coquena mientras dura la instancia y se borra al detenerla. |
| **Preset** | Un comportamiento concreto de un modelo (nombre visible al cliente, companions, parámetros). Hereda de la configuración del modelo. Se puede clonar a otro modelo. |
| **Instancia** | Un `llama-server` vivo lanzado para un servidor con una versión de su familia (sin versión explícita, la última instalada): proceso, PID, INI generado, logs. |
| **Primer plano / daemon** | Los dos modos de lanzar una instancia, como en Docker: adjunto a la terminal siguiendo logs, o desacoplado en segundo plano. |
| **Home** | Junto al ejecutable si existe ahí (modo portable); si no, `~/.coquena`. El directorio donde Coquena guarda backends, configuración, catálogo de parámetros, y por cada instancia su INI generado, PID y logs. |
| **Parámetro** | Un flag de `llama-server`. Depende de la versión y la distribución, no de la familia. |
