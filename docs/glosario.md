# Glosario

| Término | Significado |
|---|---|
| **Distribución** | Identificada por el repositorio de origen; `oficial` es el alias implícito de `https://github.com/ggml-org/llama.cpp`. Junto al release permite asociar parámetros. En el núcleo sólo se descubren y descargan binarios oficiales; un repositorio de terceros puede declararse como identidad, no como fuente de descargas o confianza. |
| **Familia de backends** | Tipo de aceleración y, cuando corresponde, versión de sus bibliotecas: `cuda@12`, `cuda@13`, `vulkan`, `cpu`. En `cuda@12`, `12` no es el release de llama.cpp. |
| **Build de llama.cpp** | Release del binario oficial, p. ej. `b6123`. Una familia puede tener varios builds; un mismo build puede publicarse para varias familias. |
| **Backend registrado** | Binario concreto con ID único y estable, familia y versión sondeada. Los externos pueden no seguir la numeración de builds oficiales. |
| **Aceleración** | Para qué se compiló el binario: `cpu`, `vulkan`, `cuda` (con major 12 o 13), `hip`, `sycl`, `openvino`… Depende del sistema operativo y la arquitectura. |
| **Dispositivo** | Una unidad de cómputo de la máquina: la CPU o una GPU concreta (NVIDIA, AMD, Intel). |
| **Modelo** | Unidad principal registrada en Coquena: un GGUF o un conjunto completo de shards. Un modelo externo no se copia; uno descargado por Coquena queda bajo su gestión. |
| **Directorio de modelos** | Área donde Coquena guarda modelos y companions gestionados. Su ubicación configurable se difiere. |
| **Externo** | Archivo o conjunto registrado por Coquena pero gestionado por otra herramienta o por el usuario. Coquena no lo mueve ni lo borra al quitar su registro. Aplica a modelos, companions y backends. |
| **Companion** | Unidad registrada para un papel auxiliar, sin dependencia de otro registro para estar disponible: proyector multimodal (`mmproj`), modelo borrador para decodificación especulativa, etc. |
| **Configuración global** | Valores de `llama-server` aplicables a cualquier backend. Raíz del eje backend. |
| **Configuración de familia** | Valores específicos de una familia; hereda de la global. |
| **Configuración de modelo** | Valores iniciales de un modelo; raíz del eje modelo. |
| **Servidor** | Composición que Coquena coordina: los presets que se ofrecen juntos, un puerto y, opcionalmente, una familia fija. Reemplaza al script lanzador. (Diseño de ejecución pendiente.) |
| **Archivo INI** | El `--models-preset` que recibe `llama-server`. Coquena lo **genera** en cada lanzamiento a partir de su modelo interno ; vive en el home de Coquena mientras dura la instancia y se borra al detenerla. |
| **Preset** | Un comportamiento concreto de un modelo (nombre visible al cliente, companions, parámetros). Hereda de la configuración del modelo. Se puede clonar a otro modelo. |
| **Instancia** | Un lanzamiento identificado por un ID propio: `llama-server`, servidor, backend concreto, puerto, PID, INI generado y logs. Varias instancias pueden provenir del mismo servidor si usan puertos distintos. |
| **Primer plano / daemon** | Los dos modos de lanzar una instancia, como en Docker: adjunto a la terminal siguiendo logs, o desacoplado en segundo plano. |
| **Home** | Directorio propio de Coquena, resuelto por las reglas de [comandos.md](comandos.md#home), incluida la prioridad de COQUENA_HOME. Separar configuración y archivos pesados configurables pertenece a una iteración posterior. |
| **Parámetro** | Opción de llama-server, aplicable a CLI, INI o router según origen y release. Su disponibilidad no se deduce de la familia de aceleración. |
