# Pantallas de la TUI

Navegación, pantallas y estilo de la TUI a 80×24. Las reglas de comportamiento
están en [interfaces visuales](../interfaces-visuales.md) y
[pantalla de servicios](../ui-servicio.md); este documento muestra cómo se ven.

- Vista previa a color en 80×24: [vista-previa.html](vista-previa.html).
  «Navegar» muestra una pantalla por vez; pestañas, nodos del árbol, botones y la
  barra de hardware llevan a la pantalla correspondiente.
- Wireframes Salt (estructura, sin color), `.salt.puml`:

| | Pantalla | Se llega desde |
|---|---|---|
| A | En vivo | pestaña ● En vivo, Ver en vivo |
| B | Definiciones › Recetas: combinaciones registradas (`combination list`), Olvidar (`combination forget`) | pestaña ◇ Definiciones, nodo Recetas |
| P | Definiciones › Presets: presets de la combinación elegida y capa de cada valor (P2: desplegable de combinación abierto) | nodo Presets |
| W | Definiciones › Modelos (inventario) | nodo Modelos, modelo de un preset |
| C | Definiciones › Modelos (búsqueda en Hugging Face) | selector Hugging Face en Modelos |
| S | Modal Agregar modelo local (`model add`) | + Agregar local… |
| O | Definiciones › Backends | nodo Backends |
| D | Hardware | barra inferior |
| E–G | Configuración › Coquena, TUI, Sistema | pestaña ≡ |
| H | Editor YAML, una pestaña por archivo de la combinación | Editar, + Agregar override, Clonar… |
| I | Log histórico (RegEx) | Histórico en el log del servicio |
| J | Carpeta sin receta | nodo Receta sin `coquena.yaml` (Inicializar) |
| K | Seleccionar backend | + Backend |
| L | Importar en Global | Importar |
| M | Definiciones › Servicios de Global | nodo Servicios (global) |
| T | Modal Agregar servicio global: nombre, backend y configuración de proceso | + Agregar en Servicios |
| M2 | Definiciones › Servicio de un proyecto: servicio de la combinación elegida y capa de cada valor | nodo Servicio (local) |
| N | Descargas: Global y proyecto actual, por ámbito y categoría | pestaña ⇣ |
| Q | Definiciones › Parámetros | nodo Parámetros |
| Q2 | Definiciones › Parámetros de un proyecto: definiciones de la combinación elegida y su capa | nodo Parámetros (local) |
| U | Definiciones › Actualizaciones (Comprobar = update, Actualizar = upgrade) | nodo Actualizaciones |
| R | Definiciones › Mantenimiento (Verificar, Limpiar) | nodo Mantenimiento |
| V | Modal Nueva combinación: YAML en orden y archivos .env | + Nueva combinación… |
| X | Modal Aceptar/Cancelar: Up con destino ocupado | ▲ Up |
| Y | Modal Sí/No/Cancelar: guardar con cambios externos | Guardar en el Editor |
| Z | Notificaciones | indicador ▲ |

El árbol sigue el orden en que se arma una definición: primero lo que se usa (backends,
servicio, modelos y parámetros) y después los presets, que los relacionan. No es un asistente:
cada vista se abre y se edita por separado.

En un proyecto, Servicio, Presets, Parámetros y Actualizaciones muestran la combinación en uso en un
desplegable arriba a la derecha (`coquena.yaml + gpu ▾`), con el mismo rótulo que Recetas.
Abierto, lista las combinaciones registradas en Recetas y marca la actual con `●`; un clic o
Enter la elige y Esc lo cierra sin cambiar. Hay una sola combinación en uso por ámbito y sesión:
elegirla en el desplegable o en Recetas cambia ambas. En Global, Presets, Parámetros y
Actualizaciones listan sus intenciones registradas y la opción «Todas las intenciones»; cada
intención se nombra por su origen: «global» para el `recipe.yaml` raíz y el `name:` de la
intención importada (por ejemplo, «global + coding»). En un proyecto, una combinación se nombra
por sus archivos; si dos se llaman igual, por su ruta desde la base del ámbito. En Servicio, Presets y Parámetros, el detalle numera los YAML de la combinación como capas
(`¹`, `²`) e indica de cuál sale cada valor.

Las combinaciones conocidas salen del registro de `.coquena`, que suma una combinación
cada vez que se usa `-f`. Desde la TUI, una combinación nueva se registra al usarla en
Up o Iniciar, con el mismo código que la CLI. Olvidar no afecta a una instancia activa
ni retira presets. Los archivos `.env` se editan como una pestaña más del Editor.

La comparación manual permite clonar un preset, modificarlo y observar. Chat
ofrece una prueba rápida con respuesta y telemetría, también disponible mediante
el comando `chat`.

## Organización

Pestañas, sin marca ni fila de atajos:

| Pestaña | Contenido |
|---|---|
| ● En vivo | Árbol servicio → proyecto → preset; detalle del servicio (puerto, estado, CPU y RAM del proceso, cola) y de la entrada (tasas, caché, especulación, contexto por slot, tareas); log del servicio. |
| ◇ Definiciones | Árbol por ámbito (Global y cada proyecto del índice; sólo el enfocado expandido). En un proyecto: Recetas, Backends, Servicio, Modelos, Parámetros, Presets, Actualizaciones y Mantenimiento; en Global: Backends, Servicios, Modelos, Parámetros, Presets, Actualizaciones y Mantenimiento. |
| ≡ (Configuración) | Categorías Coquena (lo mismo que `coquena config`), TUI (filtro del log, recursos de la barra inferior) y Sistema (`system probe`). |

Las dos últimas filas son hardware medido por dispositivo: CPU, RAM, y por GPU uso,
memoria dedicada y compartida. Hardware ampliado (D) se abre desde esa barra.

## Unidades de dato

- **Servicio:** puerto, estado, CPU/RAM del grupo de procesos, cola y log.
- **Entrada publicada** (`proyecto/preset`): `/metrics?model=` y `/slots?model=` en modo
  router; tasas, caché, especulación, contexto por slot y tareas.
- **Dispositivo:** uso (motor más ocupado), memoria dedicada y compartida (PDH + DXGI).
  No hay fuente para GPU por proceso ni para la colocación de un modelo en un dispositivo;
  la colocación sólo existe como argumentos declarados.

## Estilo

- Zonas por color de fondo, sin bordes ni líneas: `bg-base` contenido, `bg-surface`
  navegación y hardware, `bg-card` títulos.
- Botones por fondo y texto: normal sobre `border`, con foco sobre turquesa. Los botones
  de una fila se separan con un espacio; uno deshabilitado conserva su lugar y su ancho.
- Casillas `☑`/`☐` en listas, tablas y formularios.
- Toda espera visible usa el spinner Braille `⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏`: junto al texto de estado
  de la vista y en el botón que la inició, que queda deshabilitado hasta terminar.
- Configuración se muestra sólo con su ícono `≡`, en el extremo derecho de la barra superior.

| Rol | Color | Uso |
|---|---|---|
| Selección | terracota `#C85A32` / `#D57958` | pestaña y vista activas |
| Foco | turquesa `#2EC4B6` | foco de teclado, botón enfocado; nunca estado |
| Éxito | verde salvia `#8FBF6A` (nuevo) | en línea, cargado, válido |
| Advertencia | ámbar `#E09F3E` + `▲` | |
| Error | rojo `#E63946` + `✗` | |
| Métrica generación / CPU-RAM / GPU | oro `#DDA15E` | |
| Métrica prompt | azul `#457B9D` | |
| Etiquetas | gris `#88929A` | |

## Tamaño de terminal y componentes

- 80×24 es el mínimo de diseño. En terminales mayores, el espacio libre se reparte
  entre las zonas de contenido (log, gráficos, tablas y editor); la barra superior,
  los títulos y las dos filas de hardware conservan su altura.
- Los indicadores `⇣` (Descargas) y `▲` (Notificaciones) de la barra superior son
  pestañas, como En vivo, Definiciones y Configuración: muestran progreso y contador en
  todo momento y se resaltan cuando están activas. Cada pestaña conserva su propio estado
  al cambiar a otra; no existe «Volver».
- Los modales son mensajes con acciones (Aceptar/Cancelar o Sí/No/Cancelar) y formularios.
  Todo modal se dibuja sobre una superficie más clara que el fondo, con un borde redondeado
  de una celda por fuera de su contenido, y mide lo que su contenido; así se distingue de la
  vista que queda detrás y de otro modal superpuesto. Ningún modal tapa la barra inferior
  de recursos.
- El Editor usa `Terminal.Gui.Editor` (aprobado): pestañas por archivo, deshacer,
  numeración y buscar/reemplazar. YAML necesita una definición de resaltado propia,
  que cubre el estilo de bloque y el de flujo (`{…}`, `[…]`).
- Validar usa la misma validación que `verify` sobre lo que el usuario ve, borradores
  incluidos, y lista todos los problemas que puede localizar; cada uno lleva a su
  línea. Las acciones de autoría que copian o modifican la receta (como Clonar)
  exigen antes una combinación válida: si no lo es, no cambian nada y el Editor
  muestra los problemas tal como el usuario dejó los borradores.
