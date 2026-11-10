---
status: complete
created: 2026-11-10
priority: medium
tags:
- tui
- dashboard
- m4
created_at: 2026-11-10T18:26:24.033470700Z
updated_at: 2026-11-10T21:51:36.696463800Z
completed_at: 2026-11-10T21:51:36.696463800Z
transitions:
- status: in-progress
  at: 2026-11-10T18:28:10.013013100Z
- status: complete
  at: 2026-11-10T21:51:36.696463800Z
---
# Completar Recetas, Inicializar y el Editor YAML en la TUI

## Objetivo

Completar la autoría de recetas desde la TUI mediante B + J + H + Y. Esta entrega forma parte de
[M4](../../docs/milestones/M4-experiencia-completa.md). Incluye:

- inicializar una carpeta sin receta;
- administrar las combinaciones registradas;
- editar sus archivos YAML y `.env` en el Editor;
- resolver al guardar los conflictos con cambios externos.

## Alcance acordado

| Vista | Entrega |
| --- | --- |
| J · Carpeta sin receta | Inicializar crea `coquena.yaml` con las mismas reglas que `init` (nombre y destino). Al terminar, el árbol pasa a mostrar la receta nueva. |
| B · Recetas | Lista de combinaciones registradas, con su identificador, su estado y sus archivos. Desde B se puede editar la combinación seleccionada en H, olvidarla (`combination forget`) y conservar el acceso a Up/Down, + Backend (K) y Ver en vivo que entregó la 083. |
| H · Editor | Una pestaña por cada archivo de la combinación y por cada `.env`. Incluye Guardar, Validar, Up/Down, Copiar/Cortar/Pegar y Cerrar, además de deshacer, numeración, buscar/reemplazar y resaltado YAML. Las líneas modificadas y los errores de validación se marcan con su ubicación. |
| Y · Cambios externos | Al guardar un archivo que cambió en disco después de abrirlo, se ofrecen tres opciones: Sí (sobrescribir), No (recargar y descartar el borrador) y Cancelar (volver sin guardar). |

- **Editor:** usa `Terminal.Gui.Editor`, ya aprobado. Se escribe una definición de resaltado YAML propia.
- **Validación:** el Editor reutiliza la lectura, composición y validación de recetas del núcleo, sin un validador exclusivo de la TUI.
- **Guardado:** es una escritura explícita del archivo. No publica presets ni inicia o reinicia servicios. Up es la acción que publica.

## Exclusiones explícitas

- **V · Nueva combinación:** el botón de B queda deshabilitado.
- **L · Importar:** el botón Importar de B queda deshabilitado.
- **P, Q y U:** la gestión de presets, el salto «al YAML» desde P, + Agregar override y Clonar quedan fuera, aunque esos accesos también lleven a H.
- **Hallazgos 009–011:** quedan fuera, igual que en la 084.
- **Edición estructurada:** no se editan campos por formulario ni se escribe por `model set` o `param set`. La autoría se hace como texto en H.
- **Sin autorización previa:** no se agregan otros paquetes ni se hacen commits, push o cambios en la instalación global.

## Fuentes y base

- **Diseño:**
  - [pantallas](../../docs/design/tui/README.md) y [vista previa](../../docs/design/tui/vista-previa.html);
  - wireframes [B](../../docs/design/tui/B-definiciones-recetas.salt.puml), [J](../../docs/design/tui/J-definiciones-inicializar.salt.puml), [H](../../docs/design/tui/H-editor.salt.puml) e [Y](../../docs/design/tui/Y-modal-cambios-externos.salt.puml).
- **Reglas:** [servicios y recetas](../../docs/design/servicios-recetas.md), [definiciones declarativas](../../docs/design/definiciones-declarativas.md), [formato de proyecto](../../docs/design/formato-proyecto.md), [interfaces visuales](../../docs/design/interfaces-visuales.md), [comandos](../../docs/design/comandos.md) y [privacidad](../../docs/design/privacidad.md).
- **Base:** la capa de lectura de la 081, Up/Down y K de la 083, y los contratos de [lectura de recetas](../045-m3-lectura-recetas/README.md) y [edición desde la CLI](../071-m3-edicion-cli/README.md).

## Diseño e integración por concretar

- [x] Comprobar `Terminal.Gui.Editor` en Native AOT antes de adoptarlo: carga de las definiciones de resaltado, trimming y advertencias. Si no es compatible, informar y detener sólo H.
- [x] Concretar cómo se detecta un cambio externo (marca de tiempo, contenido o hash) y qué pasa si el archivo se borra o se renombra mientras está abierto.
- [x] Revisar qué ofrece el núcleo para validar un borrador antes de guardarlo y para ubicar los errores por línea. No inventar posiciones cuando la fuente no las da.
- [x] Contrastar Editar, Olvidar y el estado de cada combinación de B con `combination list/forget` y con el registro de `.coquena`. Olvidar no afecta a una instancia activa.
- [x] Definir qué pasa al cerrar con cambios sin guardar, y qué hace Up/Down desde H cuando hay un borrador o un error de validación. Si la documentación no lo resuelve, consultar.

## Plan

- [x] Completar J y la actualización del árbol después de inicializar.
- [x] Completar B: selección, detalle, Olvidar y acceso a H.
- [x] Integrar el Editor con pestañas, resaltado YAML, validación y guardado.
- [x] Implementar Y y los casos de borrado y renombrado del archivo en disco.
- [x] Validar recorridos, fallos y aislamiento de ámbitos; registrar la evidencia y el reporte de hallazgos.

## Criterios de aceptación

- [x] **Vista:** B, J, H e Y mantienen la propuesta visual a 80×24 y el Editor aprovecha una terminal mayor. El recorrido completo se puede hacer con teclado y con clic, y el foco se ve en color.
- [x] **Inicializar:** J crea la receta que crearía `init` y rechaza una carpeta que ya tiene una. Un error no se presenta como éxito.
- [x] **Olvidar:** B quita la combinación del registro sin retirar presets ni afectar instancias activas.
- [x] **Guardar:** H guarda sólo cuando el usuario lo pide y conserva el contenido que no se editó. Un `.env` se edita como otra pestaña y sus valores no aparecen en notificaciones ni en la evidencia.
- [x] **Validar:** la validación usa las reglas del núcleo y muestra cada error con su ubicación cuando la fuente la da. Un archivo inválido no se publica.
- [x] **Cambios externos:** Y protege los cambios externos. Cancelar no escribe nada, No descarta el borrador y Sí sobrescribe. Se verifica también con el archivo borrado o modificado durante la sesión.
- [x] **Sin efectos implícitos:** ningún recorrido publica presets ni inicia o reinicia servicios sin una acción explícita de Up.
- [x] **AOT:** pruebas pertinentes y publicación Native AOT win-x64 con ejecución del binario, recorriendo inicializar, editar, validar, guardar y el conflicto externo sobre ámbitos aislados.
- [x] **Revisión visual y documentación:** comparación visual con el usuario, evidencia anonimizada y límites explícitos. Enlaces locales y `git diff --check` correctos.
- [x] **Hallazgos:** reporte de hallazgos en el chat, sin implementar ni registrar hallazgos sin autorización. La tarjeta se cierra sólo con el trabajo y la evidencia completos.

## Estado y autorización

Implementación autorizada, completada y validada por el usuario.

Evidencia: B, J, H e Y en `src/Coquena/DashboardEditor.cs` y `src/Coquena/DashboardCli.cs`;
validación y guardado de borradores en `src/Coquena/Recipes/RecipeDrafts.cs`. Las pruebas
del panel pasan (188 comprobaciones, con `tests/Coquena.Tests/RecipeEditorTests.cs`); la
publicación Native AOT win-x64 compila sin advertencias y el binario se ejecuta. El usuario
recorrió la TUI y dio por válida la implementación.

Decisiones de integración tomadas:

- `Terminal.Gui.Editor` 2.5.4 publica en Native AOT win-x64 sin advertencias de trimming,
  con una definición de resaltado YAML propia cargada desde XML.
- Un cambio externo se detecta comparando los bytes del disco con los que se abrieron.
  Si el archivo se borró o se renombró, Y ofrece crearlo de nuevo o cerrar sin guardar.
- La validación lee los borradores en memoria con `RecipeReader` y las comprobaciones de
  `RecipeEdit`; informa el primer error que da el núcleo, con línea si la fuente la da.
- Guardar una combinación inválida pide confirmación. Up y Down desde H usan los archivos
  guardados: con borradores pendientes piden guardar antes, y Up no sigue si no valida.
- Cerrar, Esc o cambiar de pestaña con cambios pendientes ofrece Guardar, Descartar o Cancelar.
