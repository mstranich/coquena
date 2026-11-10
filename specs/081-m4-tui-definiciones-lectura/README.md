---
status: complete
created: 2026-11-09
priority: medium
tags:
- tui
- dashboard
- m4
created_at: 2026-11-09T23:20:21.093942400Z
updated_at: 2026-11-10T00:11:38.909720800Z
completed_at: 2026-11-10T00:11:38.909720800Z
transitions:
- status: in-progress
  at: 2026-11-09T23:38:51.475294400Z
- status: complete
  at: 2026-11-10T00:11:38.909720800Z
---
# Llevar Definiciones a la vista previa como capa de sólo lectura

## Objetivo

Que la pestaña ◇ Definiciones y sus vistas se vean como en la
[vista previa](../../docs/design/tui/vista-previa.html) a 80×24, mostrando lo que
ya se puede consultar, sin acciones de modificación. Las acciones se acuerdan en
entregas posteriores. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Alcance

Árbol por ámbito (Global y cada proyecto del índice; sólo el enfocado expandido) y
las vistas que lo consultan, según las [pantallas de la TUI](../../docs/design/tui/README.md):

| | Vista | Contenido de lectura |
|---|---|---|
| B | Recetas | combinaciones registradas y cuál está publicada |
| M | Servicios / Servicio | servicios del ámbito y su definición |
| P | Presets | presets del ámbito y su contribución |
| W | Modelos (inventario) | modelos locales del ámbito |
| O | Backends | backends instalados |
| Q | Parámetros | catálogo de parámetros |
| N | Descargas | transferencias registradas y su estado |
| U | Actualizaciones | último resultado conocido, sin comprobar |
| J | Carpeta sin receta | aviso de que falta `coquena.yaml` |

Los botones de la vista previa (Olvidar, Editar, Clonar, + Agregar, Importar,
Comprobar, Actualizar, Verificar, Limpiar, Inicializar, Up/Down, Preparación
privada) se muestran en su lugar pero deshabilitados: no reciben foco ni clic.

Fuera de alcance: C (búsqueda en Hugging Face), R (Mantenimiento, sólo acciones),
H Editor, modales K, L, S, V, X, Y y la pestaña ≡ Configuración.

## Fuentes

- Wireframes en [docs/design/tui](../../docs/design/tui/README.md): B, J, M, N, O, P, Q, U, W.
- Reglas: [interfaces-visuales](../../docs/design/interfaces-visuales.md),
  [privacidad](../../docs/design/privacidad.md).
- Base: [080](../080-m4-tui-en-vivo/README.md), que dejó el marcado de la vista
  previa (`Markup`, `Canvas`, `Lines`) y movió Up/Down y Preparación privada a
  Definiciones.

## Criterios de aceptación

- [x] El árbol y cada vista del alcance coinciden con la vista previa en zonas,
      orden, paleta, glifos y alineación, sin desbordes a 80×24.
- [x] Cada vista lee datos con las mismas operaciones de aplicación que la CLI;
      un dato sin fuente queda en blanco y no se inventa.
- [x] Ninguna vista modifica estado, archivos ni servicios; todos los botones
      aparecen deshabilitados.
- [x] Pruebas pertinentes en verde; Native AOT publicado y ejecutado recorriendo
      el árbol con teclado y clic.

## Notas

- Up/Down y Preparación privada, hoy activos en Definiciones (080), quedan
  deshabilitados como el resto de los botones (decisión del usuario).
- Árbol: mostrar todos los ámbitos que se puedan leer; si el índice de proyectos
  no es legible, sólo Global y el proyecto local (decisión del usuario).
- Vistas sin fuente (por ejemplo U o N): mostrar lo que se pueda; si no hay dato,
  la zona queda en blanco (decisión del usuario).
- Botón deshabilitado: texto gris (`mut`) sin fondo de botón (decisión del usuario).
- Verificar al empezar qué vistas tienen fuente disponible (por ejemplo historial
  de descargas o resultado de actualizaciones) e informar las que no.

## Avance 2026-11-09

- Árbol por ámbito y vistas B, J, M, N, O, P, Q, U, W y Mantenimiento en filas de marcado
  (`Observation/DefinitionsPresentation.cs`); lecturas en `DashboardDefinitions.cs` con las
  mismas operaciones que `combination list`, `model list`, `backend list`, `param list` y
  `download list`. U muestra el último plan registrado sin revalidarlo.
- Todos los botones se dibujan con la clase `off` (gris, sin fondo ni acción); el bloque
  Up/Down/Preparación privada heredado de la 080 ya no se muestra.
- Verificado: 116 comprobaciones del dashboard, incluida una lectura real sobre un home y
  un proyecto temporales que no escribe nada salvo el lock de `param list`. Native AOT
  publicado y recorrido con clic: Global (Servicios, Presets, Modelos, Backends,
  Parámetros, Descargas, Actualizaciones), un proyecto (Recetas, Presets, Servicio), una
  carpeta sin receta y un proyecto inaccesible.
- Sin verificar: comparación lado a lado con la vista previa a 80×24 exactos (la consola
  usada era mayor) y recorrido sólo con teclado.
- Sin fuente: columna USO de modelos y backends, detalle por fila (no hay selección dentro
  de la página), especulación de vigencia del plan de actualizaciones.

## Cierre 2026-11-09

El usuario dio la tarea por cerrada y verificó la comparación a 80×24 con la vista
previa y el recorrido con teclado. Queda abierta la decisión de si Presets de Global
lista también lo publicado por proyectos.
