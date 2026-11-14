---
status: complete
created: 2026-11-12
tags:
- m4
- yaml
- schema
created_at: 2026-11-12T03:19:47.499002100Z
updated_at: 2026-11-14T03:57:50.558309200Z
completed_at: 2026-11-14T03:57:50.558309200Z
transitions:
- status: planned
  at: 2026-11-14T03:41:15.977124800Z
- status: in-progress
  at: 2026-11-14T03:41:16.197771600Z
- status: complete
  at: 2026-11-14T03:57:50.558309200Z
---
# Cerrar M4 revisando el formato YAML de las recetas

## Objetivo

Dejar el formato de receta claro para uso diario antes de cerrar
[M4](../../docs/milestones/M4-experiencia-completa.md): nombres de campos que
digan lo que hacen, reflejados en el schema que genera
[090](../090-m4-tui-editor-mejoras/README.md).

## Problema

- `target` indica en qué servicio se publican los
  presets ([servicios y recetas](../../docs/design/servicios-recetas.md)), pero el
  nombre no lo dice: agentes y usuarios no lo asocian con el servicio que aloja
  los presets.
- `arg-defs` no describe que define los parámetros disponibles para los presets
  ([catálogo de parámetros](../../docs/design/catalogo-de-parametros.md)).
- `args-mode` no expresa que selecciona la política de validación de parámetros
  (`permissive` o `strict`).
- La documentación y el lector rechazan destinos por nombre. Esa restricción
  contradice la intención original reafirmada por el usuario: un servicio global
  también debe poder seleccionarse por su nombre registrado. Corregirla alinea
  código y documentación con esa intención, no introduce una decisión nueva.
- Una receta puede conservar su definición de servicio propio y publicar en un
  global. El caso que motivó la revisión fue una receta de noticias trasladada
  al servidor global sin borrar la definición local.

## Selección acordada

- Se mantienen `global` (también por omisión: servicio global predeterminado) y
  `self` (único servicio propio de la receta; requiere `service`).
- `default` es un alias de `global`, igual que en los selectores de servicio de la
  CLI, que ya lo reservan. Por eso ningún servicio global puede llamarse `default`.
- Otro nombre, como `gpu-secundaria`, se busca en el catálogo global y permite publicar
  en ese servicio aunque no sea el predeterminado. Si no existe, se informa el
  error sin sustituirlo por otro servicio.
- Los nombres explícitos del selector de publicación no buscan servicios locales.
  El único propio se selecciona mediante `self`. No se incorporan varios servicios
  locales ni un nombre de servicio independiente del proyecto.
- El propio toma `name` o, si se omite, el nombre de la carpeta, conservando las
  reglas de identidad registrada. Importado al catálogo global, se puede elegir
  allí por su nombre.
- Conservar `service` no determina el destino de los presets. Seleccionar el
  destino no inicia por sí solo el servicio ni elimina la definición propia.

## Alcance propuesto

- Corregir la selección por nombre global en documentación y código, incluyendo
  publicación e importación. Mantener la retirada por la identidad del destino
  registrado, aunque después cambie la intención de la receta.
- Renombrar `target`, `arg-defs` y `args-mode`, sin ninguna compatibilidad con los nombres
  anteriores: un nombre viejo es un campo no reconocido, como cualquier otro.
- Hacer que el schema generado por [090](../090-m4-tui-editor-mejoras/README.md)
  explique la receta por sí solo, para que un agente sin acceso a docs/design la
  entienda:
  - cada campo, no solo los renombrados, describe para qué sirve, su valor por
    omisión y los valores permitidos;
  - la raíz incluye `title` y `description`, dice qué es una receta de Coquena y
    remite al manual de usuario publicado;
  - `examples` en los campos menos obvios (`publish-to`, una entrada de `parameters`);
  - las descripciones resumen las reglas canónicas sin reemplazarlas: herencia de
    `strict-args`, combinación de capas y selección del destino de publicación.
- Actualizar documentación funcional, ejemplos, recetas de prueba y textos de la TUI.
- Aplicar el renombrado de `args-mode` en raíz, `service.server` y presets,
  convirtiendo `permissive`/`strict` en `false`/`true` y conservando el default,
  la herencia y la precedencia.
- Integrar sobre el validador y schema de 090 (dependencia de ejecución), sin
  duplicarlos. El schema admite nombres; la existencia del servicio se comprueba
  al resolver el destino operativo, no para abrir o editar una receta.

## Puntos de implementación observados

- `RecipeStructure` rechaza nombres y `RecipeTests` fija ese rechazo. La resolución
  global por nombre ya existe en `EffectiveRecipe.ResolveTarget`, `PublicationCli`
  y `RecipeRegistry.ResolveGlobalService`.
- `RecipeImporter` elige el predeterminado al importar presets sin servicio; la
  TUI también lo exige. Revisar ambos para respetar el global nombrado.
- El control en `ScopedServiceCli` tiene búsqueda local por nombre. No trasladar
  esa precedencia al selector de publicación; no se rediseñan aquí los selectores
  de control y observación.

## Por concretar

- [x] `target` se renombra a `publish-to`: se lee como frase con sus tres tipos
  de valor (`global`/`default`, `self` o un nombre global).
- [x] `arg-defs.definitions` se aplana en `parameters`, un mapa directo
  nombre → definición, igual que `presets`, `models` y `companions`. La
  envoltura `definitions` desaparece. Cambian también las rutas de edición
  de la TUI y la CLI y las claves de validación.
- [x] `args-mode` pasa a ser el booleano `strict-args`: `true` equivale a
  `strict` y `false` (por omisión) a `permissive`. Sigue el patrón de `strict`
  en `tsconfig.json`. Deja de ser un enum, así que un tercer modo exigiría
  otro campo.
- [x] Las recetas globales ya importadas las migra el usuario a mano. El código
  no incluye migración ni reimportación automática: un archivo con nombres
  viejos falla con el error habitual de campo no reconocido.

## Criterios de aceptación

- [x] Los nombres viejos se rechazan como campos no reconocidos.
- [x] `global`, `default` y la omisión seleccionan el predeterminado; `self` selecciona el
      único propio y exige `service`.
- [x] Con dos globales, seleccionar uno por nombre publica allí aunque no sea
      el predeterminado; un nombre inexistente falla sin fallback. Un nombre
      local coincidente no desvía la búsqueda global.
- [x] Conservar `service` no altera el destino global elegido. Importar presets
      sin servicio respeta el global nombrado; retirar conserva el destino registrado.
- [x] `strict-args` reemplaza a `args-mode` en sus tres ubicaciones, conserva
      el default permisivo, la herencia y la precedencia, y rechaza los valores
      no booleanos.
- [x] El schema generado refleja los nombres nuevos y rechaza los viejos.
- [x] Ningún campo del schema queda sin descripción; la raíz tiene `title` y
      `description`, y las descripciones coinciden con las reglas vigentes
      (sin el antiguo "global o self"). Las descripciones están en inglés,
      idioma prioritario mientras el schema admita uno solo.
- [x] El schema admite nombres de servicios sin depender del catálogo local.
- [x] Pruebas de lectura, validación e importación con los nombres nuevos.
- [x] Publicación Native AOT y ejecución del binario.

## Evidencia

- Suite completa `dotnet run --project tests/Coquena.Tests`: exit 0. Incluye:
  - lectura: `publish-to` con `global`, `default`, `self` y un nombre; nombre inválido
    rechazado; resolución sin sustituto ante un nombre inexistente;
  - rechazo de `target`, `args-mode` y `arg-defs` como claves desconocidas, y de
    `strict-args` no booleano;
  - schema: ninguna propiedad sin descripción, `title`, enlace al manual, `examples`
    y ausencia de los nombres viejos;
  - importación de presets sin servicio hacia un global nombrado que no es el
    predeterminado, y error `unknown-service` sin publicar en otro;
  - publicación con `up` en un global nombrado no predeterminado, con bloque
    `service` presente; `down` retira de ese destino; nombre inexistente falla.
- Native AOT: `dotnet publish src/Coquena -c Release -r win-x64` y ejecución de
  `coquena.exe`: `init` genera el schema con las claves nuevas y los ejemplos;
  `preset list` lee una receta con `publish-to: gpu-secundaria`, `strict-args` y
  `parameters`; `target` y `args-mode` fallan con `structure/unknown-key`.
- El manual de usuario (`docs/user-guide`) no se modifica: se actualiza en el release.
