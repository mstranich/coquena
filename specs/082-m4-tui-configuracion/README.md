---
status: complete
created: 2026-11-09
priority: medium
tags:
- tui
- dashboard
- m4
updated_at: 2026-11-10T13:36:34.344572900Z
completed_at: 2026-11-10T13:36:34.344572900Z
transitions:
- status: in-progress
  at: 2026-11-10T00:47:10.273998700Z
- status: complete
  at: 2026-11-10T13:36:34.344572900Z
---

# Llevar Configuración a la vista previa

## Objetivo

Que la pestaña ≡ Configuración y sus tres categorías se vean como en la
[vista previa](../../docs/design/tui/vista-previa.html) a 80×24, con los datos que
ya ofrece la aplicación. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Alcance

Árbol de categorías y vistas, según las [pantallas de la TUI](../../docs/design/tui/README.md):

| | Categoría | Contenido | Fuente existente |
|---|---|---|---|
| E | Coquena | directorios, observación, logs, parámetros | `config models-dir/backends-dir/observer-retry/inference-history/log-retention show` |
| F | TUI | filtro del log, recursos de la barra inferior | preferencia de filtro (079); recursos de la barra sin persistencia |
| G | Sistema | versión, plataforma, CPU, RAM, GPU, CUDA, Vulkan | `system probe` y muestreo de hardware |

Fuera de alcance: modales y acciones de Definiciones.

## Fuentes

- Wireframes E, F y G en [docs/design/tui](../../docs/design/tui/README.md).
- Reglas: [interfaces-visuales](../../docs/design/interfaces-visuales.md),
  [privacidad](../../docs/design/privacidad.md).
- Base: [079](../079-m4-tui-refactor-diseno/README.md) (Configuración parcial) y
  [081](../081-m4-tui-definiciones-lectura/README.md) (marcado y botones `off`).

## Criterios de aceptación

- [x] Las tres categorías coinciden con la vista previa en zonas, orden, glifos y
      alineación, sin desbordes a 80×24.
- [x] Cada valor se lee y se escribe con las mismas operaciones que la CLI; un dato
      sin fuente muestra «—».
- [x] Todas las acciones de E, F y G funcionan (decisión del usuario).
- [x] Pruebas pertinentes en verde; Native AOT publicado y ejecutado recorriendo
      las categorías con clic.

## Notas

- Alcance: todas las funciones de las tres categorías (decisión del usuario).
- Las casillas de recursos ocultan o muestran CPU, RAM y cada lectura de GPU en la
  barra inferior (decisión del usuario). Se guardan en `config.json` como
  `hiddenBarResources` (`cpu`, `ram`, `gpuN.use|memory|shared`); una GPU con sus
  tres lecturas ocultas sale de la barra.
- E: los campos se aplican con Guardar; Restablecer carga los predeterminados sin
  guardarlos y Recargar descarta lo editado. Un valor igual al predeterminado se
  guarda como predeterminado. El cambio de directorio pregunta trasladar, conservar
  en origen o cancelar, y confirma sobrescrituras, como la CLI.
- El histórico de tareas se aplica a la sesión abierta desde la próxima tarea.

## Avance 2026-11-09

- `Observation/SettingsPresentation.cs` (filas de Sistema y claves de la barra),
  `DashboardSettings.cs` (formularios de Coquena y TUI) y barra inferior con recursos
  ocultos en `LivePresentation.HardwareBar`.
- Verificado: 128 comprobaciones del dashboard, incluida la preferencia de barra
  sobre un home temporal. La suite completa se detiene en una comprobación de ayuda
  previa a este cambio (línea de `config inference-history` de más de 80 columnas).
- Native AOT publicado y recorrido con clic: Guardar del histórico (confirmado con
  `config inference-history show`), Restablecer + Guardar, Simular reset, ocultar y
  mostrar CPU y GPU1 en la barra, Sistema y Sondear de nuevo.
- Verificado por el usuario: cambio de directorios y Restablecer catálogo aplicado.
- Sin verificar: recorrido sólo con teclado y foco visible en una terminal con color
  (la consola usada no mostraba color y el control enfocado quedaba invisible).
- Ajuste tras la revisión: el árbol de categorías no lleva el título «Categorías» y
  empieza una fila más arriba; los bocetos E, F y G y la vista previa lo reflejan.
