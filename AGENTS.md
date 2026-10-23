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

## Navegación del código

- Consultar [src/code-map.yaml](src/code-map.yaml) para localizar funcionalidades,
  puntos de entrada, implementación y pruebas; verificar después el código relevante.
- Cualquier agente puede ampliarlo dentro del trabajo autorizado: actualizar entradas
  afectadas por cambios y agregar recorridos útiles que todavía no estén cubiertos.
- Mantener nombres de campos e identificadores en inglés, rutas relativas a la raíz
  del repositorio y referencias a archivos existentes y comprobados, sin números de línea.
- El mapa es parcial y descriptivo: no acredita cobertura ni sustituye la búsqueda,
  las reglas funcionales o LeanSpec. No inventariar cada clase, anticipar implementación
  ni introducir reglas arquitectónicas. Si no cubre la consulta, buscar normalmente.

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

## Privacidad de la evidencia pública

- Aplicar [Privacidad y datos compartibles](docs/privacidad.md) al redactar
  documentación, tarjetas, logs y capturas versionados. Ese documento mantiene
  las reglas de anonimización, reproducción y autoría pública.
- Conservar las ubicaciones reales de pruebas sólo en el chat o en evidencia
  local excluida de Git; revisar el contenido preparado para commit, incluidos
  archivos que Git ya rastrea.

## Validación y entrega

- Revisar manualmente alcance, estado, dependencias y evidencia mediante board/search/view.
- Documentación: comprobar sintaxis PlantUML, ejemplos JSON, enlaces locales y git diff --check.
- Código futuro: pruebas pertinentes y resultados reproducibles según la entrega acordada.
- Las tareas de runtime incluyen publicación Native AOT y ejecución del binario para
  el destino acordado; compilar/pruebas administradas no sustituyen esa evidencia.
- Comprobar dependencias en ese camino antes de adoptarlas; no posponer AOT al final.
- Informar cambios, verificaciones realmente ejecutadas, limitaciones y siguiente paso.
- No presentar un borrador, un diagrama válido o una compilación como funcionalidad terminada.

## Reporte de hallazgos para Code Review

Al entregar una tarea, incluir en el chat un reporte orientado a hallazgos que merecen
evaluación; la evidencia en la tarjeta no sustituye este reporte. Su propósito es
facilitar la revisión del código y evaluar consecuencias funcionales, técnicas y de
escalabilidad, además de informar las pruebas ejecutadas. Las decisiones tomadas son
contexto del hallazgo, no una lista de tareas pendientes.

- Distinguir decisiones resueltas por los requisitos, sin consecuencias que requieran
  seguimiento, de hallazgos que revelan restricciones, contradicciones, asunciones o
  posibilidades de mejora. Informar brevemente las primeras; desarrollar los segundos
  sin presentar toda decisión de implementación como un problema.
- Para cada hallazgo, incluir un título concreto, qué se observó, cómo quedó implementado,
  el fundamento de la decisión (documentación canónica, código existente o asunción
  propia), su impacto y qué queda por evaluar. Distinguir hechos comprobados de riesgos
  o hipótesis; que las pruebas pasen no demuestra que la política elegida sea la deseada.
- Las dudas pueden aparecer durante el trabajo. Consultar primero la documentación,
  la tarjeta y sus dependencias; si no resuelven una decisión que afecta comportamiento
  o alcance, preguntar y detener sólo el trabajo afectado. El reporte final no sustituye
  esa consulta ni convierte una asunción en una regla aprobada.
- Explicar los cambios en documentación funcional: qué problema revelan y si aclaran
  una regla existente o incorporan una decisión nueva. No adaptar las reglas al código
  sin señalarlo expresamente para su evaluación.
- Considerar el milestone actual y los siguientes documentados, incluso cuando sólo
  declaren intenciones y todavía no tengan tareas definidas. Señalar decisiones que
  puedan condicionar su evolución; distinguir impactos comprobados de riesgos o
  hipótesis. Ese horizonte no autoriza anticipar funciones ni ampliar la tarea.
- Describir el problema antes de proponer una solución. Un hallazgo no es automáticamente
  una tarea ni un compromiso: primero se evalúa si merece seguimiento en un registro
  de hallazgos separado de LeanSpec. Varios hallazgos pueden analizarse juntos y alimentar
  una propuesta común que luego origine una única tarea o las entregas necesarias.
- Señalar relaciones entre hallazgos cuando ayuden a ese análisis. No crear registros,
  propuestas o tarjetas ni absorber su implementación sin autorización; el reporte
  deja las cuestiones disponibles para evaluación y no constituye otro backlog de tareas.
- Ajustar el detalle a lo encontrado. Si no hubo hallazgos relevantes, indicarlo brevemente;
  no inventar dudas ni tareas para completar el reporte. Cerrar la entrega indicando
  validación ejecutada, limitaciones y estado de revisión/commit.
