---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '002'
created_at: 2026-10-07T22:02:14.121129700Z
updated_at: 2026-10-08T01:34:22.301135900Z
completed_at: 2026-10-08T01:34:22.301135900Z
transitions:
- status: in-progress
  at: 2026-10-08T01:20:09.061492800Z
- status: complete
  at: 2026-10-08T01:34:22.301135900Z
---
# Hardware y selección de backend oficial

## Objetivo
Detectar plataforma/hardware y consultar metadatos oficiales con pruebas simuladas. Resolver latest y release explícito por variante; mostrar elección automática y alternativas. No descargar ni instalar.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Probar prioridad release antes de hardware, CUDA@12 distinto de release, fallback CPU nativo y exclusión automática de unknown/incompatible. Selección explícita respeta plataforma; diagnósticos no afirman capacidades no detectadas.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.
Las familias oficiales incluidas son las delimitadas en el
[alcance de M1](../../docs/milestones/M1-servidor-local.md#alcance-propuesto).

## Evidencia
Verificado el 7 de octubre de 2026, SDK 10.0.401, Windows x64.

### Entrega y decisiones técnicas

- `system` inspecciona plataforma nativa, nombre de CPU, RAM física y adaptadores;
  el nombre/fabricante del adaptador no se convierte en evidencia de aceleración.
  `backend remote [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest]` consulta
  sólo metadatos oficiales y presenta selección, artefactos y alternativas.
  No descarga archivos de backend, instala, crea operaciones ni escribe defaults/home.
- Componentes pequeños en `src/Coquena/Backends`, CLI en `BackendCli.cs`, sin paquetes
  nuevos ni cambios en los contratos persistidos. Resolver puro con hardware y
  releases inyectables; HTTP simulado por `HttpMessageHandler` en pruebas.
- Interop generado con `LibraryImport` y estructuras ABI, habilitando
  `AllowUnsafeBlocks` únicamente para llamadas/punteros nativos. DLLs desde System32,
  no desde el directorio de trabajo; sin WMI, PowerShell ni CUDA Toolkit como dependencia.
- CUDA sondea `cuInit`, versión máxima soportada por el driver, dispositivos y SM.
  Vulkan sondea loader/instancia 1.2, dispositivos GPU, cola compute y almacenamiento
  de 16 bits; excluye dispositivos Vulkan de tipo CPU como aceleración.
  API ausente/fallo incompleto es `unknown`, no prueba de ausencia de hardware.
  Enumeración completa sin dispositivos o requisitos básicos incumplidos es `incompatible`.
- CUDA 12/13 distingue requisitos básicos de driver y SM; driver de la misma versión
  mayor pero anterior al toolkit queda `unknown`, sin presumir compatibilidad menor.
  Toolkits/arquitecturas futuros o cobertura PTX incierta se diagnostican como `unknown`.
  La salida aclara que el sondeo básico no garantiza inferencia ni valida el binario
  concreto: la comprobación `llama-server --version` pertenece a la instalación 006.
- Primero se ordena el build numéricamente; sólo en el mismo build se prefiere CUDA
  compatible más alta, luego Vulkan y CPU nativo. Entre toolkits de la misma versión
  mayor se ordena su versión menor, sin cambiar la variante `cuda@<mayor>`.
  Sólo `compatible` entra en automático. Familia explícita conserva `unknown` o
  `incompatible`, pero nunca selecciona otra plataforma ni un conjunto incompleto.
- Se reconoce el ZIP principal y su cudart del mismo release/toolkit/arquitectura;
  se admiten nombres cudart con/sin build. Una dependencia ausente o ambigua se informa
  y excluye de selección. El digest remoto de backends no se usa ni persiste.
- Corrección detectada por ejecución real: `/releases/latest` devuelve el marcador
  estable `v0.6.0`, no el build ejecutable más reciente. Los builds `bNNNN` actuales
  son prereleases. Se consulta el listado público, se ignoran marcadores no bNNNN y
  se toma el mayor build de la primera página con builds. Si no hay selección dentro
  del filtro, se consultan tags numéricos descendentes, tolerando huecos 404.
  Un release explícito consulta únicamente su tag, sin sustituirlo.
- Límites técnicos: páginas de 30 releases, hasta ocho páginas para hallar builds;
  hasta 32 builds descendentes, incluidos huecos, para resolver un filtro; 4 MiB por
  respuesta, 512 artefactos por release, 32 niveles JSON, 20 s por petición real y
  60 s por consulta. Alcanzar el límite informa búsqueda incompleta, no inexistencia
  de candidatos más antiguos; se puede consultar un release explícito.
  Sin reintentos automáticos, autenticación ni cache persistida.
- Se rechazan JSON/tipos inválidos, duplicados, tamaños negativos y URLs que no
  corresponden al artefacto/tag en `ggml-org/llama.cpp`. Sin redirecciones HTTP en
  consulta real. Rate limit, desconexión, timeout y cancelación tienen diagnóstico.

### Verificaciones reproducibles

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/tests/win-x64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true
& artifacts/tests/win-x64/Coquena.Tests.exe artifacts/publish/win-x64/coquena.exe
& artifacts/publish/win-x64/coquena.exe system
& artifacts/publish/win-x64/coquena.exe backend remote
& artifacts/publish/win-x64/coquena.exe backend remote cuda@12 --release latest
& artifacts/publish/win-x64/coquena.exe backend remote vulkan --release b11476
git diff --check
```

- Runner administrado: 180 comprobaciones de backends, más regresiones de CLI,
  contratos, almacenamiento y las 53 comprobaciones de download, todas OK.
- CLI y runner publicados Native AOT sin warnings/errores, con warnings como errores.
  Runner nativo: 181 comprobaciones de backends (incluye proceso real `system`),
  nueve casos internos y nueve ejecuciones de CLI publicada; regresiones existentes
  OK, incluidas 54 comprobaciones de download con ejecución nativa.
- Fixtures pequeños en `tests/Coquena.Tests/BackendTests.cs`: prioridad numérica
  de release, filtros/latest/pin, CUDA mayor/menor, fallback CPU, desconocido versus
  incompatible, plataformas ajenas, familias fuera de alcance, dependencia faltante,
  alias de nombres cudart, JSON/URL inválidos, HTTP 403/404/429/500, desconexión,
  límite de consulta/tamaño, cancelación, marcador estable y prereleases bNNNN.
- Recorrido nativo con proveedor real: CPU Intel i9-13900K, 32534 MiB de RAM,
  RTX 3080 Ti (SM 8.6, driver admite CUDA 13.4) e Intel UHD 770.
  Vulkan enumeró ambas GPUs con los requisitos básicos. Automático eligió
  `ggml/cuda@13:b11476`; filtro CUDA 12 eligió `ggml/cuda@12:b11476` y pin Vulkan
  eligió `ggml/vulkan:b11476`, con sus artefactos correctos.
  Consultas ejecutadas con `COQUENA_HOME` temporal inexistente: no se creó el home.
- Enlaces locales y `git diff --check` OK. No se agregaron/modificaron JSON de
  ejemplo ni diagramas PlantUML; no requieren nueva validación de sintaxis aquí.
- Alcance, dependencia 002 completa y evidencia revisados manualmente con
  `board/search/view`. `deps` y `tokens` devolvieron que aún no están migrados al
  adaptador; no se presentan como validaciones realizadas.

### Evidencia del proveedor y límites

- [API de releases GitHub](https://docs.github.com/en/rest/releases/releases),
  [listado oficial](https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=5),
  [release b11476](https://github.com/ggml-org/llama.cpp/releases/tag/b11476).
- [cuDriverGetVersion](https://docs.nvidia.com/cuda/cuda-driver-api/cuda_driver_api/group__CUDA__VERSION.html),
  [compatibilidad CUDA](https://docs.nvidia.com/deploy/cuda-compatibility/610/minor-version-compatibility.html),
  [builds oficiales](https://github.com/ggml-org/llama.cpp/blob/b11476/.github/workflows/release.yml),
  [arquitecturas CUDA](https://github.com/ggml-org/llama.cpp/blob/b11476/ggml/src/ggml-cuda/CMakeLists.txt),
  [requisitos Vulkan](https://github.com/ggml-org/llama.cpp/blob/b11476/ggml/src/ggml-vulkan/ggml-vulkan.cpp).
- Sólo recorrido Windows x64 de M1. El resolver representa otras plataformas para
  rechazarlas; la CLI no habilita instalación/ejecución multiplataforma.
  Reconocimiento conservador de nombres actuales oficiales Windows: no se adivina
  la familia de archivos históricos/desconocidos. El orden de builds recientes se
  toma del listado oficial observado; no se pretende indexar todos los releases históricos.
- No hay descarga/extracción/sondeo de `llama-server`, registro de instalación, selección
  persistida, externos, update/upgrade ni garantía de inferencia. M1 continúa pendiente.
  Entrega aceptada por el usuario y commit autorizado al cierre.
  Siguiente tarea habilitada: 006, instalación de backend oficial.
