# Coquena — instrucciones para agentes

## Contexto y alcance

- Leer docs/README.md y los documentos canónicos del área antes de trabajar.
- docs/ contiene las reglas funcionales; docs/milestones/ define entregables y alcance.
  specs/ contiene tareas concretas y evidencia, enlazadas a su milestone sin duplicar reglas.
- La documentación funcional/general describe el comportamiento y las reglas del producto,
  independientemente de la planificación. No incluir en ella números o estados de tareas,
  recortes por entrega, dependencias de implementación ni avisos como «queda pendiente
  de una entrega posterior» o «fuera del alcance de la tarea». Esos datos pertenecen
  exclusivamente a specs/ y docs/milestones/. Al ajustar el alcance de una tarea, actualizar
  allí la planificación; cambiar la documentación funcional sólo si cambia una regla del
  producto, expresándola como comportamiento sin narrar cuándo o en qué tarea se implementará.
- Reinicio en C# con publicación Native AOT desde la primera entrega, en pasos pequeños
  y comprensibles. No consultar ni depender de legacy.
- No iniciar código, agregar paquetes, ampliar alcance ni hacer commits sin autorización.
- No resolver contradicciones por omisión: informar y detener sólo el trabajo afectado.

## LeanSpec

Adaptador local markdown, specs/ y .lean-spec/config.json. La CLI disponible es leanspec.
Antes de una tarea: leanspec board y leanspec search <tema>; leer la tarjeta si existe.
Crear tarjetas con leanspec create <nombre>, nunca con archivos manuales.
Editar el cuerpo de una tarjeta está permitido; modificar metadatos con la CLI.

| Acción | Comando |
|---|---|
| Crear | leanspec create <nombre> |
| Consultar | leanspec view <spec> |
| Estado/metadatos | leanspec update <spec> --status <estado> |
| Dependencia | leanspec rel add <spec> --depends-on <otra> |
| Retirar dependencia | leanspec rel rm <spec> --depends-on <otra> |
| Dependencias | leanspec deps <spec> |
| Tokens | leanspec tokens <spec> |

Planned → in-progress antes de implementar → complete sólo con trabajo y evidencia completos.
Redactar una tarjeta no completa su implementación. Depends-on expresa un bloqueo real;
una referencia a documentación o una mención no crea por sí sola una dependencia.
Mantener cada entrega acotada; enlazar las reglas canónicas en vez de copiarlas.

## Validación y entrega

- Revisar manualmente alcance, estado, dependencias y evidencia mediante board/search/view.
- Documentación: comprobar sintaxis PlantUML, ejemplos JSON, enlaces locales y git diff --check.
- Código futuro: pruebas pertinentes y resultados reproducibles según la entrega acordada.
- Las tareas de runtime incluyen publicación Native AOT y ejecución del binario para
  el destino acordado; compilar/pruebas administradas no sustituyen esa evidencia.
- Comprobar dependencias en ese camino antes de adoptarlas; no posponer AOT al final.
- Informar cambios, verificaciones realmente ejecutadas, limitaciones y siguiente paso.
- No presentar un borrador, un diagrama válido o una compilación como funcionalidad terminada.
