---
status: archived
created: 2026-11-13
priority: medium
tags:
- i18n cli tui
- m5
created_at: 2026-11-13T16:33:37.861730400Z
updated_at: 2026-11-18T03:11:37.767780100Z
completed_at: 2026-11-16T18:42:06.735375Z
transitions:
- status: draft
  at: 2026-11-13T16:33:49.578233300Z
- status: planned
  at: 2026-11-16T14:11:59.846538800Z
- status: in-progress
  at: 2026-11-16T14:12:00.111637300Z
- status: complete
  at: 2026-11-16T18:42:06.735375Z
- status: archived
  at: 2026-11-18T03:11:37.767780100Z
---
# Internacionalizar los textos de usuario

## Objetivo

Que los textos que muestran la CLI y la TUI puedan traducirse sin tocar la lógica que
los decide, y que las pruebas no dependan de su redacción.
Entrega de [M5](../../docs/milestones/M5-rendimiento.md).

## Situación inicial

- [091](../091-m4-cli-salida-legible-espacio/README.md) creó `src/Coquena/Messages.cs`:
  una clase estática que reúne textos de usuario. El código decide con códigos y
  datos, la clase los convierte en palabras, y las pruebas comparan códigos, datos o
  entradas de la clase.
- La clase sólo contiene los textos que alguna prueba comprobaba y los nuevos de 091.
  La mayoría de los mensajes de la CLI y la TUI sigue escrita en cada archivo.
- Los errores siguen la forma `Error: texto (código).`; el código no se traduce.
- El estado persistido guarda texto ya redactado: `update-plan.json` conserva el
  diagnóstico de compatibilidad como frase, así que un plan viejo sigue mostrando la
  redacción vieja y en el idioma en que se escribió. Para traducir, el estado debería
  guardar códigos y datos, y la redacción hacerse al mostrar.
- Las vistas de datos sin `--json` (`show`, `combination list`, de `TextTree.cs`)
  muestran las claves tal como están en los datos, en inglés (`service-id`,
  `installation-id`). Traducirlas requiere etiquetas por clave.

## Alcance

- Llevar a `Messages` todos los textos de usuario de la CLI y la TUI, incluido `--help`.
- Idiomas entregados: `en-US` y `es-AR` (redactado en español neutro).
- Guardar códigos y datos en lugar de frases en el estado persistido (planes de
  actualización y cualquier otro documento que hoy guarde texto para mostrar).
- `config language show|set <código>|unset|list|export <código>` y selector en la pestaña
  «Configuración» de la TUI. El cambio se aplica al reiniciar la TUI.
- Fuera de alcance: códigos de error, salida `--json`, logs técnicos, contenido de las
  recetas, claves de datos de `show`/`combination list` y las descripciones del catálogo
  de parámetros que provienen de llama.cpp.

## Decisiones

- **Mecanismo:** el inglés vive en el código como entradas de `Messages`; la clave de
  cada texto es el nombre del miembro. Los demás idiomas son JSON con esas claves:
  `es-AR` va empotrado en el binario y se pueden agregar archivos externos en
  `languages/<código>.json`, junto a la configuración global.
- Un archivo externo con el código de un idioma empotrado lo reemplaza. Una clave faltante
  se muestra en inglés. Un archivo inválido o con marcadores distintos no se carga y se avisa.
- **Elección:** sólo la configuración (CLI y TUI); `auto` es el valor predeterminado.
  `auto` elige el código exacto de la cultura del sistema, luego otra variante del mismo
  idioma y, si no hay ninguno, `en-US`.
- **TUI:** traducción directa, sin rediseño; sólo se corrigen textos que se corten.
- `export <código>` escribe un JSON con todas las claves, útil como plantilla.
- Los códigos y las pruebas son independientes del idioma. Los formatos regionales
  son los del sistema operativo; seleccionar idioma sólo cambia los textos.
- No se requiere migración ni retrocompatibilidad con documentos anteriores.
- Otros idiomas también pueden empotrarse. Si el idioma configurado no está
  disponible o no puede cargarse, se avisa y se usa inglés sin borrar la preferencia.

## Criterios de aceptación

- [x] Ningún texto de usuario fuera de `Messages`.
- [x] Las pruebas no comparan frases escritas a mano.
- [x] Prueba de consistencia: cada idioma empotrado tiene las claves y marcadores del inglés.
- [x] El estado persistido guarda códigos y datos, no frases.
- [x] `config language` y el selector de la TUI funcionan con idiomas empotrados y externos.
- [x] Publicación Native AOT y ejecución del binario con `auto`, `es-AR` y `en-US`.

## Implementación y evidencia

- Catálogo de 2589 entradas: inglés en `Messages`, español en JSON empotrado.
  Se retiró la duplicación temporal de español en el código. El proyecto admite
  empotrar otros JSON y descubrir archivos externos por código.
- Se valida el archivo externo completo antes de activarlo: claves duplicadas o
  desconocidas, valores no string y marcadores incompatibles lo rechazan. El fallback
  conserva la preferencia y avisa; export incluye las claves faltantes en inglés.
- Los diagnósticos de compatibilidad, actualización e integridad conservan clave y
  argumentos. Las descripciones predeterminadas propias conservan `DescriptionKey`;
  upstream y contenido escrito por el usuario se conservan literalmente.
- Identificadores de vistas, acciones y disponibilidad separados de sus rótulos.
  Se eliminó la inferencia de cascadas y tipos de fallo a partir de frases.
- `dotnet build tests/Coquena.Tests --no-restore`: sin warnings ni errores.
- `dotnet publish src/Coquena -c Release -r win-x64 -o $PublishRoot`: publicación
  Windows x64 Native AOT y ejecución con auto, es-AR y en-US.
- `Coquena.Tests --localization-native-tests $PublishRoot/coquena.exe`: 19 checks;
  selección, reinicio, ayuda, export y fallback ante archivo externo inválido o ausente.
- TUI Native AOT en terminal interactiva: apertura en español, Configuración › TUI,
  selección de en-US, conservación del idioma de la sesión y apertura en inglés al
  reiniciar. Verificación acotada a esos recorridos; no acredita cada diálogo.
- Dashboard: 258 checks en cada idioma; permanece un fallo de RecipeWriter al
  escribir mapas intermedios. Se reprodujo la misma comprobación en una copia
  limpia de HEAD, sin los cambios de internacionalización.
- `git diff --check` y enlaces locales de la documentación modificada comprobados.
  Sin cambios en PlantUML. La CLI `leanspec deps` informó que aún no está migrada;
  revisión manual de tarjeta, board y referencias de milestone.

- Revisión de claves: los nombres mecánicos (`Presentation*`, `*Texts*`) se
  reemplazaron por nombres según su significado y las frases armadas por
  concatenación pasaron a una entrada con marcadores. Sólo quedan concatenadas
  etiquetas cuyo valor lleva otro estilo de la TUI.
- Suite completa `Coquena.Tests` con idioma del sistema (es-AR) y con
  `--language en-US`: exit 0. Dashboard: 280 checks en cada idioma; único fallo,
  el preexistente de RecipeWriter.
- Publicación Native AOT final y `--localization-native-tests`: 19 checks, exit 0.

La evidencia usa fixtures aislados; no acredita ejecución de un backend real ni
rendimiento del equipo. No se hizo commit.
