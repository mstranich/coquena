# Glosario

| Término | Significado |
|---|---|
| **Backend** | Una instalación de llama.cpp administrada por Coquena: una *versión* (build `bNNNN` del release) en una *variante*. Pueden convivir varios. |
| **Variante** | La aceleración para la que se compiló el binario: `cpu`, `vulkan`, `cuda@12`, `cuda@13`, `hip`, `sycl`, `openvino`… Depende del sistema operativo y la arquitectura. |
| **Dispositivo** | Una unidad de cómputo de la máquina: la CPU o una GPU concreta (NVIDIA, AMD, Intel). |
| **Modelo** | Un archivo GGUF principal (o un conjunto de shards) disponible en disco. |
| **Companion** | Un GGUF que acompaña a un modelo sin funcionar solo: proyector multimodal (`mmproj`), modelo borrador para decodificación especulativa, etc. |
| **Preset** | Una forma nombrada de servir un modelo: modelo + companions + parámetros (contexto, capas MoE en CPU, dispositivo…). Es el nombre que el cliente pide. |
| **Instancia** | Un `llama-server` configurado: un backend, un puerto, parámetros globales y uno o más presets. |
| **Ejecución** | Una corrida concreta de una instancia: proceso del sistema, PID, logs. Una instancia tiene como mucho una ejecución viva. |
| **Primer plano / daemon** | Los dos modos de lanzar una instancia, como en Docker: adjunto a la terminal siguiendo logs, o desacoplado en segundo plano. |
| **Home** | El directorio donde Coquena guarda backends, configuración, PIDs y logs. |
