# Coquena — análisis funcional

Coquena es un **manager de línea de comandos para llama.cpp**, en la familia de SDKMan,
NVM, FNM o UV: instala y convive con varias versiones y variantes de llama.cpp, y
coordina con qué modelos y companions se levanta cada `llama-server`.

Reemplaza los scripts lanzadores (`.bat`, `.ps1`, `.sh`) que hoy se escriben a mano para
arrancar `llama-server` con la combinación correcta de binario, modelos y parámetros.

Esta carpeta contiene el análisis funcional. No define implementación: el traslado a
código ocurre después, a partir de estos diagramas.

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
| [estados.md](estados.md) | Ciclos de vida (instancia, backend) |
| [actividades.md](actividades.md) | Flujos de los casos de uso principales |
| [secuencias.md](secuencias.md) | Interacciones entre CLI, Coquena, sistema y llama-server |

Los diagramas son PlantUML empotrado en Markdown. Ante contradicción entre un diagrama y
el texto, manda el diagrama y el texto se corrige.
