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
| P | Definiciones › Presets | nodo Presets |
| W | Definiciones › Modelos (inventario) | nodo Modelos, modelo de un preset |
| C | Definiciones › Modelos (búsqueda en Hugging Face) | selector Hugging Face en Modelos |
| S | Modal Agregar modelo local (`model add`) | + Agregar local… |
| O | Definiciones › Backends | nodo Backends |
| D | Hardware | barra inferior |
| E–G | Configuración › Coquena, TUI, Sistema | pestaña ≡ Configuración |
| H | Editor YAML, una pestaña por archivo de la combinación | Editar, + Agregar override, Clonar… |
| I | Log histórico (RegEx) | Histórico en el log del servicio |
| J | Carpeta sin receta | nodo Receta sin `coquena.yaml` (Inicializar) |
| K | Seleccionar backend | + Backend |
| L | Importar en Global | Importar |
| M | Definiciones › Servicios | nodo Servicios (global) o Servicio (local) |
| T | Modal Agregar servicio global: nombre, backend y configuración de proceso | + Agregar en Servicios |
| N | Descargas: Global y proyecto actual, por ámbito y categoría | pestaña ⇣ |
| Q | Definiciones › Parámetros | nodo Parámetros |
| U | Definiciones › Actualizaciones (Comprobar = update, Actualizar = upgrade) | nodo Actualizaciones |
| R | Definiciones › Mantenimiento (Verificar, Limpiar) | nodo Mantenimiento |
| V | Modal Nueva combinación: YAML en orden y archivos .env | + Nueva combinación… |
| X | Modal Aceptar/Cancelar: Up con destino ocupado | ▲ Up |
| Y | Modal Sí/No/Cancelar: guardar con cambios externos | Guardar en el Editor |
| Z | Notificaciones | indicador ▲ |

Las combinaciones conocidas salen del registro de `.coquena`, que suma una combinación
cada vez que se usa `-f`. Desde la TUI, una combinación nueva se registra al usarla en
Up o Iniciar, con el mismo código que la CLI. Olvidar no afecta a una instancia activa
ni retira presets. Los archivos `.env` se editan como una pestaña más del Editor.

Fuera de la TUI: la comparación de ejecuciones queda en manos del usuario (clonar un
preset, modificarlo y observar) y se vincula con `benchmark`.

## Organización

Tres pestañas, sin marca ni fila de atajos:

| Pestaña | Contenido |
|---|---|
| ● En vivo | Árbol servicio → proyecto → preset; detalle del servicio (puerto, estado, CPU y RAM del proceso, cola) y de la entrada (tasas, caché, especulación, contexto por slot, tareas); log del servicio. |
| ◇ Definiciones | Árbol por ámbito (Global y cada proyecto del índice; sólo el enfocado expandido) con Recetas o Servicios, Presets, Servicio, Modelos, Backends, Parámetros, Actualizaciones y Mantenimiento. |
| ≡ Configuración | Categorías Coquena (lo mismo que `coquena config`), TUI (filtro del log, recursos de la barra inferior) y Sistema (`system probe`). |

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
- Botones por fondo y texto: normal sobre `border`, con foco sobre turquesa.

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
  numeración y buscar/reemplazar. YAML necesita una definición de resaltado propia.
