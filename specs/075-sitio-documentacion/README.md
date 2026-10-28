---
status: in-progress
created: 2026-10-28
priority: medium
tags:
- docs
- sitio
created_at: 2026-10-28T11:09:03.576917700Z
updated_at: 2026-10-28T11:25:28.639304Z
transitions:
- status: in-progress
  at: 2026-10-28T11:25:28.639304Z
---
# Publicar la documentación funcional como sitio Zensical en GitHub Pages

## Objetivo

Publicar `docs/` como sitio de documentación en `https://mstranich.github.io/coquena/`, con la identidad visual de Coquena, construido y desplegado desde este repositorio. La página de producto sigue en `mstranich.github.io` (`/es/coquena/`, `/en/coquena/`) y enlaza a este sitio; esta tarjeta no la modifica.

## Fuente de verdad

- [README.md](../../docs/README.md): índice y punto de entrada de la documentación funcional.
- [privacidad.md](../../docs/privacidad.md): reglas de contenido compartible; publicar no cambia qué puede versionarse.
- Paleta de marca definitiva (colores del design system de Coquena): terracota `#C85A32`/`#B24C26`, turquesa `#2EC4B6`/`#25A89C`, dorado `#DDA15E`/`#C98E4B`, fondos `#14171A`/`#1E2328`/`#252B32`, borde `#3E4651`, texto `#EAEBED`/`#88929A`, estados `#2EC4B6`/`#E09F3E`/`#E63946`/`#457B9D`. El resto del CSS de referencia (clases, tipografías, botones) es orientativo y no se adopta como definitivo.

La documentación funcional no se reescribe para el sitio: el sitio la presenta tal como está.

## Trabajo y aceptación

- [x] `zensical.toml` en la raíz con `site_url = "https://mstranich.github.io/coquena/"` definido en un único lugar, navegación basada en el índice de `docs/README.md` y versión de Zensical fijada (0.0.73). `docs_dir` apunta a la copia preparada `artifacts/docs-site/docs`, no a `docs/`, para que el render no toque las fuentes.
- [x] Esquema oscuro propio (`coquena`) con `primary`/`accent = "custom"` y `extra_css` que mapea la paleta de marca a variables `--md-*`; banner `docs/assets/coquena-banner.jpg` en la portada, agregado sólo en la copia.
- [x] Contraste AA en texto: barra en superficie con borde terracota (blanco sobre terracota da 4,23:1, bajo AA), terracota sólo en títulos h1/h2 (4,25:1, AA para texto grande), enlaces turquesa (8,30:1), hover dorado (8,00:1), texto principal 15,08:1 y atenuado 5,68:1 sobre `#14171A`. Calculado con la fórmula WCAG 2.x.
- [x] PlantUML renderizado a SVG con `plantuml.jar` 1.2026.8 (SHA-256 fijado) y Graphviz, tanto bloques ```` ```plantuml ```` embebidos como archivos `.salt.puml` (enlaces reescritos a `.svg`). Las fuentes en `docs/` no se modifican.
- [x] Workflow `.github/workflows/docs.yml`: push a `main` con filtro de rutas y ejecución manual; permisos `contents: read`, `pages: write`, `id-token: write`; Java, uv, Graphviz, `docs-site/build.py`, `zensical build --clean --strict`, `upload-pages-artifact`, `deploy-pages`.
- [x] `docs/milestones/` y `docs/implementacion-catalogo-modelos.md` fuera de la navegación principal; se publican igual y los enlaces que salen de `docs/` (p. ej. a `specs/`) se reescriben a GitHub.
- [ ] Enlaces locales de `docs/` resueltos en el sitio publicado (sin 404 internos) y diagramas visibles en los cinco documentos con PlantUML y en los tres wireframes Salt. Acreditado en build local; falta el sitio desplegado.
- [x] README del repositorio con enlace a la documentación publicada.

Paso manual del responsable del repositorio: en Settings → Pages elegir «GitHub Actions» como fuente.

## Fuera de alcance

- Cambios en `mstranich.github.io` (el enlace desde la página de producto se hace allí).
- Dominio propio; la configuración debe permitir migrar cambiando `site_url` y agregando `CNAME`.
- Traducción de la documentación; el sitio se publica en español.
- Convertir diagramas PlantUML a Mermaid.

## Decisiones a confirmar durante la entrega

- Resuelto: el CSS vive en `docs-site/assets/` y el build lo copia a la copia preparada; `docs/` no recibe archivos del sitio.
- Inclusión futura de una sección «Roadmap» basada en `docs/milestones/`.

## Evidencia

Local, 2026-10-28:

- `uv run docs-site/build.py`: copia preparada con 15 diagramas SVG (12 embebidos y 3 Salt).
- `uvx zensical@0.0.73 build --clean --strict`: «No issues found». Las 15 advertencias iniciales de anclas con tildes se resolvieron con `pymdownx.slugs.slugify`, que conserva Unicode como GitHub.
- Revisión en navegador de portada (banner y colores) y `estados/` (diagramas visibles); enlaces Salt y enlaces a `specs/` comprobados en el HTML generado.
- `git status docs/`: sin cambios en las fuentes.
- Resaltado de código: el esquema propio heredaba los colores de sintaxis del tema claro (ilegibles sobre surface). Se definieron las variables `--md-code-hl-*` sólo con colores de marca de contraste ≥ 4,5:1 sobre `#1E2328`: dorado 7,04, turquesa 7,30, warning 6,94, atenuado 5,00, texto 13,27. Terracota (3,74), danger (3,80) e info (3,45) quedan fuera del código. Números y constantes usan `--coquena-primary-text` `#D57958`, terracota aclarado del mismo tono (5,06 sobre surface, 4,56 sobre card, 5,74 sobre base); es un color nuevo propuesto, pendiente de incorporarse a la paleta en Figma.
- Diagramas: `docs-site/plantuml-coquena.puml` se aplica con `-config` al render (fondo transparente sobre la página sin marco, cajas card con borde turquesa como los actores, texto `#EAEBED`, flechas atenuadas, notas y decisiones con borde dorado, inicio/fin turquesa). Revisados actividades, secuencia, clases y estados. Salt ignora skinparam y `<style>`; los tres wireframes se renderizan sin el estilo, como boceto claro.

Pendiente: activar Pages con «GitHub Actions», ejecutar el workflow y revisar el sitio desplegado.

## Validación prevista

Ejecutar el workflow en GitHub, revisar el sitio publicado en `https://mstranich.github.io/coquena/` (portada, navegación, búsqueda, diagramas, enlaces internos y contraste) y registrar la URL del run y las comprobaciones realizadas. Previsualizar localmente con `uvx zensical serve` / `uvx zensical build` antes del push; el build local no sustituye la evidencia del sitio desplegado. Comprobar `git diff --check` y que el render no deje cambios en las fuentes de `docs/`.
