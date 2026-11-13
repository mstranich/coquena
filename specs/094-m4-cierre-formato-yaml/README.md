---
status: draft
created: 2026-11-12
tags:
- m4
- yaml
- schema
created_at: 2026-11-12T03:19:47.499002100Z
updated_at: 2026-11-12T03:19:47.499002100Z
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
- Otro nombre, como `Pepito`, se busca en el catálogo global y permite publicar
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
- Descripción y valores permitidos de los campos renombrados en el schema
  generado por [090](../090-m4-tui-editor-mejoras/README.md).
- Actualizar documentación funcional, ejemplos, recetas de prueba y textos de la TUI.
- Aplicar el renombrado de `args-mode` en raíz, `service.server` y presets,
  conservando valores, defaults, herencia y precedencia.
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

- [ ] Nuevo nombre de `target`, que exprese dónde se publican los presets.
  Se conversaron `service-to-publish`, `publish-to` y `publish-presets-to`;
  ninguno está aprobado ni limita las alternativas.
- [ ] Nuevo nombre de `arg-defs`, explícito y sin abreviaturas poco claras.
  Se conversaron `definitions`, `parameter-definition` y `parameter-definitions`;
  ninguno está aprobado ni limita las alternativas.
- [ ] Nuevo nombre de `args-mode`; `parameter-validation` es una propuesta,
  no una decisión aprobada.
- [ ] Evaluar el nombre del bloque de parámetros junto con su envoltura interna
  `definitions`. Aplanarla sería un cambio estructural adicional, no acordado.
- [ ] Si las recetas globales importadas se migran o se reimportan.

## Criterios de aceptación (borrador)

- [ ] Los nombres viejos se rechazan como campos no reconocidos.
- [ ] `global` y la omisión seleccionan el predeterminado; `self` selecciona el
      único propio y exige `service`.
- [ ] Con dos globales, seleccionar uno por nombre publica allí aunque no sea
      el predeterminado; un nombre inexistente falla sin fallback. Un nombre
      local coincidente no desvía la búsqueda global.
- [ ] Conservar `service` no altera el destino global elegido. Importar presets
      sin servicio respeta el global nombrado; retirar conserva el destino registrado.
- [ ] El renombrado de `args-mode` conserva valores y herencia en sus tres ubicaciones.
- [ ] El schema generado refleja los nombres nuevos y rechaza los viejos.
- [ ] El schema admite nombres de servicios sin depender del catálogo local.
- [ ] Pruebas de lectura, validación e importación con los nombres nuevos.
- [ ] Publicación Native AOT y ejecución del binario.
