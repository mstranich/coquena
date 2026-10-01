# Coquena — análisis funcional

Coquena es un **manager de línea de comandos para llama.cpp**, en la familia de SDKMan,
NVM, FNM o UV: instala y convive con varias versiones y variantes de llama.cpp, y
coordina con qué modelos y companions se levanta cada `llama-server`.

Reemplaza los scripts lanzadores (`.bat`, `.ps1`, `.sh`) que hoy se escriben a mano para
arrancar `llama-server` con la combinación correcta de binario, modelos y parámetros.

## Por qué nace

Nace de una necesidad propia. Catapult, LM Studio y Ollama no simplifican la configuración
de llama.cpp para exprimir el hardware disponible; el resultado era mantener funciones de
PowerShell que lanzaban `llama-server` con líneas de comando cada vez más largas.

Tres descubrimientos dan forma al proyecto:

- **Un archivo INI enruta varios modelos en un solo `llama-server`** (`--models-preset`).
  Intercambiar modelos en VRAM dentro del mismo proceso es potencialmente más rápido que
  cerrar el servidor y relanzarlo con otro modelo.
- **Lanzar `llama-server` directamente ahorra recursos**: sin el overhead de los wrappers.
- **`llama-server` expone métricas** (formato Prometheus) aprovechables en un dashboard.

Existen enrutadores como llama-swap, pero apuntan a otro problema. llama.cpp sirve igual
para un servidor local, un laboratorio personal o un equipo productivo; Coquena quiere que
**los presets que un desarrollador afina en su PC sean migrables** a otra escala de
ejecución. Por eso, a largo plazo, aparecen una TUI (Terminal.Gui, cuando el núcleo esté
maduro) y una Web GUI para hardware remoto.

## Alcance de este análisis

Esta carpeta contiene el análisis funcional. No define implementación: el traslado a
código ocurre después, a partir de estos diagramas.

El reinicio se implementará en **C#**, en pasos pequeños y revisables por quien mantiene
Coquena. Primero se cerrarán las reglas de backends, presets y ejecución; la identidad,
actualización y procedencia de modelos frente a Hugging Face se tratarán después, como
un problema separado. Las soluciones de `legacy-go` son experiencia, no arquitectura
que deba copiarse.

## El contexto es la máquina

A diferencia de NVM o UV, el contexto no es una carpeta ni un proyecto: es **el hardware
de la PC donde corre**. No hay `.nvmrc` ni shims. Lo que importa es qué CPU y qué GPUs hay
(posiblemente de fabricantes distintos) y qué binarios de llama.cpp pueden usarlas.

## Cómo se usa lo que Coquena levanta

Un `llama-server` levantado por Coquena expone **varios presets con nombre**. El servidor
gestiona por sí mismo qué modelo está cargado en la GPU; el cliente (OpenCode, Pi Code, u
otro harness agéntico) alterna entre presets pidiendo uno u otro por nombre.

Un caso típico: modelos de frontera para diseño e investigación, y modelos locales para
implementación — con un preset liviano y rápido y otro pesado con más contexto, ambos
sobre el mismo GGUF.

## Documentos

| Documento | Contenido |
|---|---|
| [glosario.md](glosario.md) | Vocabulario del dominio |
| [casos-de-uso.md](casos-de-uso.md) | Actores y casos de uso |
| [modelo-de-datos.md](modelo-de-datos.md) | Diagrama de clases del dominio |
| [estados.md](estados.md) | Ciclos de vida (instancia, backend, modelo, preset) |
| [modelos-y-companions.md](modelos-y-companions.md) | Por qué el estado de un modelo es un solo eje, y qué quedó afuera |
| [actividades.md](actividades.md) | Flujos de los casos de uso principales |
| [comandos.md](comandos.md) | Superficie de la línea de comandos |
| [catalogo-de-parametros.md](catalogo-de-parametros.md) | Parámetros de llama-server por versión y distribución |

Los diagramas son PlantUML empotrado en Markdown y muestran primero el flujo o la
estructura. El texto breve aclara sus condiciones y excepciones. **No deben
contradecirse**: si discrepan, se corrigen ambos antes de implementar; ninguno prevalece
automáticamente. Una hipótesis se rotula como tal hasta verificarla.
