---
status: in-progress
created: 2026-11-05
priority: medium
tags:
- docs
- sitio
created_at: 2026-11-05T13:15:15.387499700Z
updated_at: 2026-11-05T13:37:44.115558700Z
transitions:
- status: in-progress
  at: 2026-11-05T13:37:44.115558700Z
---
# Separar el manual de usuario de la documentación funcional

## Objetivo

Ofrecer un manual de usuario en inglés para quien quiera usar Coquena con recetas y la línea de comando, separado de la documentación funcional en castellano. El manual es lo primero que ve cualquier persona que llega al sitio; desde allí puede profundizar en el roadmap y en la documentación funcional.

## Fuente de verdad

- [docs/design/README.md](../../docs/design/README.md): índice actual de la documentación funcional.
- [privacidad.md](../../docs/design/privacidad.md): se aplica también a los ejemplos del manual.
- [075-sitio-documentacion](../075-sitio-documentacion/README.md): sitio, build y workflow sobre los que se trabaja.
- [README.md](../../README.md): contenido de uso que se traslada al manual.

## Estructura acordada

Estructura física:

```text
docs/
├── user-guide/   # manual de usuario, en inglés
├── design/       # documentación funcional, en castellano
├── milestones/   # planificación, en castellano
└── assets/
```

Estructura del sitio, definida por `nav` e independiente de las carpetas:

```text
Manual (páginas de primer nivel)   /coquena/
├── Roadmap (sección plegada)      /coquena/milestones/
└── Design (sección plegada)       /coquena/design/
```

| Sección | Contenido | Se publica desde |
|---|---|---|
| `user-guide/` | Sólo lo disponible en la versión publicada, con ejemplos probados contra el binario | Tag de la versión |
| `design/` | Qué hace y cómo debe ser la aplicación, conceptualmente | `main` |
| `milestones/` | Objetivos y entregas, presentados como roadmap | `main` |

Las propuestas y RFC son ideas no aprobadas o no incorporadas: no pertenecen a `design/`. Sus carpetas (`proposals/`, `rfcs/`) se crean cuando exista la primera; esta tarjeta no las agrega.

## Trabajo y aceptación

### Reorganización

- [x] Mover con `git mv` la documentación funcional a `docs/design/`, sin renombrar ni reescribir sus documentos. La única excepción es `implementacion-catalogo-modelos.md`, que pasa a `design/guia-tecnica.md` porque ya no trata sólo del catálogo; actualizar sus enlaces en `docs/`, `specs/` y la configuración del sitio.
- [x] `docs/milestones/` y `docs/assets/` permanecen en su lugar; actualizar sólo los enlaces afectados.
- [x] Actualizar las referencias a las rutas movidas en `AGENTS.md`, `README.md`, `src/code-map.yaml`, `specs/` (incluidas las archivadas), `zensical.toml`, `docs-site/build.py` y `.github/workflows/docs.yml`.
- [x] Redefinir en `AGENTS.md` el papel de cada carpeta y que el manual se actualiza en cada release con tag, sin cambiar reglas de producto.

### Manual

- [x] Crear `docs/user-guide/` en inglés, documentando la versión 0.3.0, con: Getting started, Global and local, Recipes, Services and backends, Models and Presets, Maintenance, Configuration, CLI reference y Troubleshooting.
- [x] Trasladar del `README.md` las recetas, los perfiles y la instalación desde fuente; el README conserva la presentación, un inicio rápido corto y los enlaces.
- [x] Ejecutar con el binario de la versión documentada los comandos y recetas del manual. Registrar la evidencia conforme a la política de privacidad.

### Sitio

- [x] `docs-site/build.py` ubica el contenido de `user-guide/` en la raíz de la copia preparada y ajusta los enlaces, de modo que `/coquena/` muestre el manual sin portada intermedia.
- [x] `nav` presenta en el menú lateral, sin pestañas, las páginas del manual y debajo Roadmap y Design; `docs/milestones/README.md`, en inglés, resume el estado de cada milestone.
- [x] El manual avisa que Roadmap y Design están en castellano.
- [x] Idiomas: Zensical 0.0.73 admite un idioma de interfaz por sitio. La interfaz queda en inglés, las páginas en castellano declaran `lang="es"` tras el build y la búsqueda cubre ambas secciones con un único índice.
- [x] La primera publicación del manual sale una vez desde `main`, porque el tag de 0.3.0 no contiene `docs/user-guide/`. Desde el primer tag que lo contenga, el build toma `user-guide/` del último tag y el resto de `main`. El workflow se ejecuta también con tags `v*`; el entorno `github-pages` debe admitirlos.
- [x] Actualizar `site_description` y los textos que hoy presentan el sitio sólo como documentación funcional.

## Fuera de alcance

- Traducir `design/` o `milestones/`.
- Cambiar reglas funcionales o reestructurar el contenido de los documentos movidos.
- Crear `proposals/` o `rfcs/`.
- Cambios en `mstranich.github.io`. Sus enlaces a `/coquena/...` se actualizan allí después de publicar.
- Redirecciones desde las URLs anteriores: el sitio es reciente y los enlaces propios se actualizan.
- Migrar a inglés los milestones existentes; la migración de idioma será gradual.

## Decisiones

- **Primera publicación:** una vez desde `main`, documentando 0.3.0.
- **Actualización del manual:** en cada release con tag. M4 corresponde a la serie 0.4.x.
- **Índice de milestones:** en inglés.
- **Guía técnica:** `implementacion-catalogo-modelos.md` se renombra a `guia-tecnica.md` y aparece como un enlace simple al final de la navegación de Design, sin protagonismo.

## Validación prevista

- `uv run docs-site/build.py` y `uvx zensical@0.0.73 build --clean --strict` sin advertencias.
- Comprobar enlaces locales en `docs/`, `specs/`, `README.md` y `AGENTS.md`, y que `src/code-map.yaml` sólo apunte a archivos existentes.
- Comprobar que no queden referencias a `implementacion-catalogo-modelos`, salvo en evidencia histórica que se decida conservar.
- `git diff --check`; comprobar con `git log --follow` que los documentos movidos conservan su historia.
- Revisar el sitio publicado: `/coquena/` muestra el manual, la navegación llega a Roadmap y Design, la búsqueda funciona y los diagramas siguen visibles.
- Comprobar que el manual publicado corresponde al tag de la versión aunque `main` tenga cambios posteriores.

## Evidencia

Local, 2026-11-05, con binario Native AOT 0.3.0 compilado desde el árbol actual (`src/` sin cambios respecto de `v0.3.0`) y `COQUENA_HOME` aislado:

- Recorrido real con backend `cpu` b12125 y `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M`: `import --as-default`, `service default start -d`, `up`, `ps`, petición de chat, `preset set`, segundo `up`, `logs`, `verify`, `down` y `service default stop`. El puerto 9931 estaba ocupado en la máquina, por lo que se usó `port: 19931`; ese fallo se documenta en Troubleshooting. La petición se envió con curl, no con `Invoke-RestMethod`.
- `up`, `down`, `apply` y `withdraw` se ejecutaron en una pseudo-terminal: sin terminal fallan con `interaction-required`.
- Comprobados también: `init`, `target: self` con dos capas `-f` (ctx-size 1024 de la capa final llegó al router), `service list|show`, `system probe`, `backend show|list`, `model list|show`, `download list|status`, `preset show|clone|unset|remove`, `update`, `update --global`, `clean --dry-run`, `verify --checksum`, `combination list`, `logs list`, `config` y `param list`.
- Sobre una home vacía, `up --start-service` detectó el hardware, descargó `cuda@13` y creó el servicio global default `coquena`; no llegó a iniciar porque el puerto 9931 estaba ocupado. El modelo directo en `presets.<nombre>.args.model` se publicó y respondió en un servicio `target: self`. `service create --global` registró un servicio. Una descarga de backend `cpu` falló con `retries-exhausted` por HTTP 504 del origen; `download resume` no se probó.
- `service default start --global -d`, `service default start -d` y `service start -d` sobre homes vacías crearon el servicio `coquena` y prepararon `cuda@13`; ninguno llegó a escuchar porque otro llama-server ocupaba el puerto 9931.
- La sección de descargas y espacio en disco sale de la ayuda, el código y `docs/design/`; sólo se ejecutaron `clean --dry-run`, `download list` y `download status`.
- Comprobados con el binario: capas con `null`, `.env` aceptado, variable requerida ausente y `args-mode: strict`. `preset show` oculta los valores interpolados, de modo que la precedencia de variables sale de `docs/design/`. El ejemplo de hooks no se ejecutó.
- Comprobados con el binario: creación de presets con `preset set`, `preset bind`, `preset clone`, `model set`, `model companion set`, escritura en la última capa y los errores de `verify` citados. `model companion download` descargó `ggml-org/gemma-3-4b-it-GGUF#mmproj:F16`; no se ejecutó un preset multimodal.
- `update` con versión fija y tras cambiarla a `latest`: el backend quedó `not-prepared` y `upgrade` lo bloqueó (código 6) sin afectar al servicio. No hubo una actualización real, porque no se dispuso de una versión anterior con `latest`; el impacto de `upgrade` sale de `docs/design/`.
- `config log-retention`, `observer-retry`, `models-dir show`, `backends-dir show` y `param-reset --dry-run` comprobados; el traslado de directorios y `param-reset` real no se ejecutaron.
- Argumentos: `minimum`/`maximum` rechazados por `verify`; `applies-to` con `max-build` rechazado por `up` y con `min-build` aceptado por Coquena (el router rechazó luego el argumento inventado). `param define` no se ejecutó.
- Sin ejecutar: servicio global y de proyecto en simultáneo, argumentos personalizados, variantes CUDA y Vulkan, ejecutable externo, companions, descargas pausadas, `upgrade`, `clean --yes` y la receta de varios perfiles.
- `uv run docs-site/build.py` y `uvx zensical@0.0.73 build --clean --strict`: «No issues found», 20 diagramas. Enlaces locales de todos los `.md`: 0 rotos.
- Selección del manual desde tag comprobada en un clon temporal.

Hallazgos registrados durante la revisión: [003](../../findings/003-publicacion-sin-interaccion.md), [004](../../findings/004-parada-sin-plazo-configurable.md), [005](../../findings/005-espacio-usado-no-consultable.md), [006](../../findings/006-verify-un-error-por-vez.md), [007](../../findings/007-formato-receta-tras-init.md) y [008](../../findings/008-aplicabilidad-de-argumentos.md).

Pendiente: admitir tags `v*` en el entorno `github-pages`, desplegar y revisar el sitio publicado.
