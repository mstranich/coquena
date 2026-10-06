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
ejecución. Después del milestone ejecutable se prevé una TUI que coordine múltiples
descargas; su librería no está elegida. Una Web GUI para hardware remoto es una idea futura.

## Alcance de este análisis

Esta carpeta contiene el análisis funcional y una guía de implementación del catálogo
para trabajo futuro delegable. No hay implementación todavía: la guía distingue los
acuerdos funcionales, los mecanismos técnicos propuestos y las decisiones pendientes.

Coquena se implementará en **C#**, en pasos pequeños y revisables por quien mantiene
Coquena. El orden de revisión es: backends y catálogo de parámetros; configuración y
presets; catálogo de modelos y sus comandos (incluida su relación con Hugging Face);
por último, servidores e instanciación. La superficie `server` y las reglas de ejecución
siguen siendo borradores, no decisiones cerradas. La documentación es autosuficiente: ningún requisito depende de otra implementación.

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

### Cómo leer y mantener esta documentación

1. **Comportamiento:** modelos-y-companions.md, descargas.md y catalogo-de-parametros.md.
2. **Interfaz:** comandos.md define sintaxis y menús; no repite los algoritmos.
3. **Diagramas:** casos-de-uso.md es el mapa; secuencias.md muestra interacciones;
   actividades.md sólo conserva flujos complementarios. Estados y modelo de datos
   explican ejes y relaciones, no otra versión de las reglas.
4. **Implementación futura:** implementacion-catalogo-modelos.md describe mecanismos
   propuestos, entregas y [pendientes](implementacion-catalogo-modelos.md#9-decisiones-que-no-deben-quedar-ocultas-en-el-código).

Una regla se mantiene en su documento de comportamiento; los demás la enlazan o
representan. Cambiarla exige revisar sus diagramas y comandos, no copiar otro párrafo.
**Servidores e instanciación siguen en borrador**, incluidos los bloques de ejecución
en comandos, actividades y estados. No implementar esos bloques como requisitos cerrados.

| Documento | Contenido |
|---|---|
| [glosario.md](glosario.md) | Vocabulario del dominio |
| [casos-de-uso.md](casos-de-uso.md) | Actores y casos de uso |
| [modelo-de-datos.md](modelo-de-datos.md) | Diagrama de clases del dominio |
| [estados.md](estados.md) | Ciclos de vida (instancia, backend, modelo, preset) |
| [modelos-y-companions.md](modelos-y-companions.md) | Registro externo, seguimiento remoto, actualización e integridad |
| [implementacion-catalogo-modelos.md](implementacion-catalogo-modelos.md) | Contratos, algoritmos, entregas y pruebas para implementar el catálogo en C# |
| [descargas.md](descargas.md) | Gestión de transferencias, persistencia, recuperación y pruebas |
| [secuencias.md](secuencias.md) | Interacciones de registro, update, upgrade, instalación, recuperación e integridad |
| [actividades.md](actividades.md) | Flujos de los casos de uso principales |
| [comandos.md](comandos.md) | Superficie de la línea de comandos |
| [catalogo-de-parametros.md](catalogo-de-parametros.md) | Parámetros de llama-server por versión y distribución |

Los diagramas son PlantUML empotrado en Markdown y muestran primero el flujo o la
estructura. El texto breve aclara sus condiciones y excepciones. **No deben
contradecirse**: si discrepan, se corrigen ambos antes de implementar; ninguno prevalece
automáticamente. Una hipótesis se rotula como tal hasta verificarla.
