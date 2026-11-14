---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
- cli
created_at: 2026-11-11T13:06:39.371700700Z
updated_at: 2026-11-13T19:00:11.764413Z
completed_at: 2026-11-13T19:00:11.764413Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.724960500Z
- status: planned
  at: 2026-11-13T13:58:46.478869500Z
- status: in-progress
  at: 2026-11-13T13:58:46.551867700Z
- status: complete
  at: 2026-11-13T19:00:11.764413Z
---
# Hacer legible la salida de la CLI y la TUI y consultar el espacio usado

## Objetivo

Que lo que Coquena muestra, en la CLI y en la TUI, se entienda en poco texto: qué pasó,
qué se puede hacer y, sólo si hace falta, el detalle técnico. El foco es el wording de
todos los comandos y vistas, no sólo de algunos. Además, una forma de conocer el espacio
en disco y una limpieza con nombre y ámbito claros.
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

- Muchos resultados de la CLI y de la TUI son técnicos y toscos: códigos internos
  (`confirmation-required`, `invalid-selection`), identificadores y JSON donde una
  persona espera una frase; mezcla de idiomas («All OK») y jerga.
- `verify` mostraba un error por vez; `update` y `upgrade` imprimen JSON
  ([hallazgo 006](../../docs/findings/006-verify-un-error-por-vez.md)).
- No hay comando para conocer el espacio total usado
  ([hallazgo 005](../../docs/findings/005-espacio-usado-no-consultable.md)).
- `import` informa «presets seleccionados confirmados» aunque no se eligieron
  presets ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).
- Los procesos largos (`up`, descargas, instalaciones) son verbosos y no muestran de
  forma compacta qué paso está en curso.

## Alcance

### Wording

- Inventario de los textos que muestran los comandos de la CLI y las vistas de la TUI:
  texto actual y propuesta, revisado por lotes. Cada texto nuevo se propone al usuario
  y se aplica sólo tras su aprobación.
- Criterios comunes: primero el resultado en una frase; después la acción posible;
  identificadores, rutas y códigos sólo cuando ayudan a actuar o con `--verbose`.
  Mismo vocabulario en la CLI y en la TUI.
- Glifos de terminal (`✔`, `✖`, `⚠`, `→`) y color de forma coherente, con una
  alternativa en texto cuando la terminal no los soporta o no hay color.
- Español neutro, sin voseo ni usted: infinitivo o forma impersonal.
- Sin aclaraciones defensivas que nieguen consecuencias que nadie supuso.
- Las pruebas comprueban códigos y datos, no textos, para permitir cambiar el wording
  y traducirlo más adelante.

### Comandos

- `verify` lista todos los problemas encontrados.
- `update` y `upgrade` con salida legible por defecto y `--json` como opción.
- Mensajes de `import` según las categorías elegidas.
- `usage` (alias `df`) recorre los inventarios de todos los ámbitos conocidos (el
  proyecto, si hay uno, el global y los proyectos registrados), mide cada instalación
  por sus archivos y señala lo libre con la regla de `prune`: ni protegido ni
  referenciado por receta, preset, servicio, plan o descarga en curso. Muestra cada
  ámbito por separado. No tiene `--global`.
- `usage` y la vista previa de `prune` no crean nada.
- `prune` reemplaza a `clean` como nombre principal; `clean` queda como alias. Borra lo
  que sobra del proyecto, y del ámbito global con `--global`. Conserva la
  previsualización, la confirmación y `--yes`.

### Progreso

- Progreso compacto al estilo npm o Docker Compose en todos los comandos con pasos: en
  una terminal interactiva, cada paso ocupa una línea con el spinner Braille, que se
  fija con `✔` o `✖` al terminar. Los pasos en paralelo se muestran como un bloque de
  líneas que se actualizan juntas. Con salida redirigida, líneas simples sin animación
  ni caracteres de control. Si un paso falla, se muestra su detalle completo.
- Un único spinner y un único modelo de pasos compartidos por la CLI y la TUI: la TUI
  dibuja los mismos pasos de `up`/`down` en En vivo.
- En la TUI, `Esc` con un proceso en curso pregunta antes de salir, porque salir lo detiene.
- `--verbose` conserva la salida completa actual. No se agrega `--quiet`.

## Decisiones

- Compatibilidad: se acepta cambiar la salida; `update` y `upgrade` pasan a texto
  legible y JSON sólo con `--json`.
- Espacio: comando propio `usage`, alias `df`, sin selección de ámbito.
- Limpieza: `prune` es el nombre principal, `clean` su alias; `--global` sólo en `prune`.
- Modo detallado: `--verbose`; sin `--quiet`.
- [Comandos](../../docs/design/comandos.md) se actualiza con esta entrega; el manual de
  usuario, en la release del milestone.

## Resuelto durante la entrega

- [x] Pasos con progreso en `up`, `down`, `apply` y `withdraw`, compartidos con la TUI.
  Las transferencias (descargas de backend y modelos, `upgrade`, `download resume`)
  usan una única línea viva con spinner.
- [x] Lotes del inventario de textos: lote 1 (CLI general), lote 2 (aclaraciones
  defensivas), lote 3 (TUI) y español neutro, aplicados tras aprobación.
- [x] Salida para personas en todos los comandos; JSON sólo con `--json`.

## Criterios de aceptación

- [x] Inventario de textos revisado; los textos cambiados fueron aprobados antes de aplicarse.
- [x] Pruebas de salida legible y JSON, con casos de varios errores.
- [x] Pruebas de `usage`/`df` con recursos en ambos ámbitos y de `prune`/`clean` con y sin `--global`.
- [x] Pruebas de progreso con salida redirigida, retención durante preguntas y paso
  fallido (`StepProgressTests`). La línea viva en terminal interactiva se verificó a mano.
- [x] [Comandos](../../docs/design/comandos.md) actualizado.
- [x] Publicación Native AOT y ejecución del binario.

## Evidencia

- 46 de 46 suites de `Coquena.Tests` pasan con el ejecutable y el runner publicados
  en Native AOT (win-x64).
- Ejecución manual del binario AOT: `backend download` con línea viva y spinner,
  Ctrl+C informado como `transfer-paused`, salidas legibles sin `--json`, confirmadas
  por el usuario.
- Textos de usuario centralizados en `src/Coquena/Messages.cs`; las pruebas comparan
  códigos, datos o el catálogo. La traducción queda en
  [096](../096-i18n-textos-de-usuario/README.md).
