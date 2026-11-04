---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 041-ayuda-cli
- 043-descarga-directa
- 056-m3-importacion
- 060-m3-limpieza
- 062-m3-seguimiento-logs
- 064-m3-observacion-continua
- 071-m3-edicion-cli
- 072-m3-init
created_at: 2026-10-21T23:26:07.360340300Z
updated_at: 2026-11-04T02:08:26.869914600Z
completed_at: 2026-11-04T02:08:26.869914600Z
transitions:
- status: in-progress
  at: 2026-11-04T00:27:43.759336900Z
- status: complete
  at: 2026-11-04T02:08:26.869914600Z
---
# Completar cobertura y ayuda de la CLI del milestone

## Objetivo

Acreditar coherencia de las familias CLI entregadas por M3, sin convertir la auditoría en implementación de dominios pendientes.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [glosario.md](../../docs/glosario.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Mantener matriz de cada comando/opción y tarjeta que lo entrega, con ámbito, efecto, selección y códigos de salida; comprobar que ningún comando requerido quede sin responsable.
- [ ] Integrar ayuda real de operaciones implementadas, aliases service/server y backend/runtime, flags ps -w/--watch y opciones locales; no inventar versiones, consultas o abreviaturas no definidas.
- [ ] Comprobar config sin proyecto, contexto global explícito, target default/self/nombrado, GUID aislados y ausencia de efectos por consultas.
- [ ] Cubrir import selectivo y reemplazo, ciclo de presets frente a proceso, pin/latest, confirmación de clean y tamaños humanos de escala 1024.
- [ ] Verificar cuantización inequívoca de 043, init opcional y edición puntual sin convertirlos en requisitos del YAML manual.
- [ ] Asignar faltantes de dominio a su tarjeta y comprobar sus pruebas antes de acreditar cobertura; mantener docs funcionales como fuente de verdad.
- [ ] Comparar la superficie pública de v0.2.1 con M3 (preview v0.3.0-alpha.1) y
  repetir las operaciones compatibles en proyecto y global. Comprobar ausencia
  de duplicados o cambios acumulativos donde el contrato prevé idempotencia.

## Inventario inicial de compatibilidad

La referencia es la ayuda del tag v0.2.1. Las equivalencias son funcionales y
usan la gramática canónica de M3.

| Familia v0.2.1 | Entrada M3 | Ámbito y efecto | Tarjetas |
| --- | --- | --- | --- |
| config | config | Perfil global, independiente de la receta | 061, 064 |
| system probe | system probe | Consulta de máquina | 048 |
| backend install/list/use/register | backend download/list/set/show, alias runtime | Inventario por ámbito; la edición declarativa selecciona backend | 048, 071 |
| model y companion | model y model companion | Inventario por ámbito, altas externas y descargas protegidas | 043, 049, 050 |
| download | download | Operaciones por GUID dentro del ámbito | 047 |
| model/backend update y upgrade | update/upgrade --select | Consulta y plan exacto; aplicación conjunta por ámbito | 058, 059 |
| model verify | verify, con --checksum para hash exhaustivo | Auditoría de receta o catálogo global | 057, 076 |
| server | service/server, ps, logs | Selección de proceso propio o global | 053, 062, 063, 064 |
| preset y param | preset y param; up/down y apply/withdraw | Edición declarativa y publicación separadas | 046, 055, 071 |
| Retirada de archivos | clean, model remove | Inventario seleccionado y comprobación de referencias | 049, 060 |
| Superficie adicional | init, import, combination | Inicialización opcional, promoción global y gestión de combinaciones | 051, 056, 060, 072 |

### Decisiones de compatibilidad

- La gramática M3 excluye `model [companion] refresh`.
- `backend remove` recupera la retirada individual por GUID del ámbito.
  Aplica las protecciones de clean y valida de nuevo el plan antes de borrar.
- Clean elimina del catálogo las instalaciones retiradas y sus entradas vacías.
  Ante confirmación requerida con entrada redirigida devuelve 3.

## Dependencias

- [041: Organizar la ayuda según la sintaxis real del ejecutable](../041-ayuda-cli/README.md)
- [043: Descargar una cuantización explícita inequívoca sin interacción redundante](../043-descarga-directa/README.md)
- [056: Importar recetas completas o selectivas con reemplazo explícito](../056-m3-importacion/README.md)
- [060: Liberar recursos sólo con propiedad y referencias comprobadas](../060-m3-limpieza/README.md)
- [062: Seguir logs por identidad de servicio entre reinicios y rotación](../062-m3-seguimiento-logs/README.md)
- [064: Observar con ps -w o --watch y reconciliar eventos](../064-m3-observacion-continua/README.md)
- [071: Persistir cambios puntuales de entidades en el YAML seleccionado](../071-m3-edicion-cli/README.md)
- [072: Inicializar una receta mínima sin fabricar estado de ejecución](../072-m3-init/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin funciones M4, comandos nuevos por analogía, reglas alternativas a los canónicos ni cambios de dominio para hacer pasar una matriz.

## Estado y evidencia

- Publicado Native AOT win-x64 y ejecutado con una home aislada en D:.
- `config log-retention show` devolvió 0 y 100 MiB antes de inicializar
  el proyecto. Repetir `init` devolvió 0 y conservó el hash del YAML.
- Repetir `list` para model, backend/runtime, download y service/server
  devolvió 0 y salida estable con y sin `--global` en el proyecto mínimo.
  La secuencia creó directorios y un lock en la home, por lo que este
  recorrido no acredita ausencia de efectos en disco.
- La ayuda Native AOT mostró el selector de update/upgrade y el destino
  registrado de down. Pendientes la matriz por opción y los escenarios
  mutantes con inventario, transferencias y servicios.
- La suite de limpieza ejecutó 35 comprobaciones con el producto Native AOT.
  Incluye catálogo sin entradas vacías tras borrar, rechazo de GUID ajeno,
  protección explícita, retirada individual por el alias runtime y código 3
  si clean requiere confirmación con entrada redirigida.
- Consultas aisladas en homes nuevas: config log-retention show, service list
  y los list globales de model, backend y download crearon la home. Los list
  de proyecto de model, backend y download no crearon la home global.

Cierre por decisión explícita del usuario, con aprobación de los cambios
entregados. Las casillas abiertas registran cobertura que quedó sin acreditar.
El recorrido imperativo global sigue incompleto y esta evidencia no acredita
la aceptación integrada de 074.

### Recorrido real global

El recorrido de aceptación debe hacerse con operaciones directas del catálogo
global. La prueba con importación de abajo acredita el router y la inferencia,
pero no satisface ese recorrido.

La importación agrega una vía de entrada al catálogo global. El recorrido
imperativo debe permitir seleccionar el backend del servicio, crear y editar
un preset global vinculado al modelo, vincularlo al servicio, desvincularlo y
retirarlo. Las operaciones históricas de referencia son `server use`,
`preset create` y `server preset add|remove|list`. Acreditar también que al
retirar las referencias y protecciones, clean libera las instalaciones y
retira sus entradas del inventario.

- En una segunda home vacía, crear el servicio, asignar default y retirar el
  servicio sin consumidores funcionó. `backend set --global --service default
  --version latest --variant cpu` y `preset list --global` devolvieron 2.
  El editor actual limita las mutaciones a YAML de proyecto.
- Tras corregir la consulta de backend, una prueba Native AOT en una home
  nueva mostró la versión y variante del servicio creado por CLI y permitió
  retirarlo. No se ejecutó importación en esa prueba.

- Home aislada mediante `COQUENA_HOME`. Se creó y designó un servicio
  predeterminado. La descarga explícita instaló llama.cpp b12089 CPU y
  Qwen2.5-0.5B-Instruct Q4_K_M (468,64 MiB).
- Importar la receta con reemplazo configuró backend y preset. El router
  inició con las instalaciones existentes y respondió inferencia. La consulta
  `backend show --global --service default` devolvió `backend-required`
  aun después de la importación que permitió arrancar.
- La API del router confirmó `unloaded` y luego `loaded`. Una carga enviada
  antes de observar la descarga completa devolvió 400. Tras esperar el estado,
  el modelo volvió a responder inferencia.
- Stop, update y upgrade global devolvieron 0. El plan clasificó ambos
  recursos sin cambios y no se aplicaron candidatos.
- La retirada del servicio quedó bloqueada mientras conservaba la contribución.
  Reimportar con el preset deshabilitado permitió retirarlo. Se olvidaron
  las combinaciones listadas y se desprotegieron ambas instalaciones.
  Clean global confirmado devolvió 0 y 0 B recuperables. Los dos recursos
  quedaron en el inventario y una nueva consulta de novedades todavía los
  seleccionó. Pendiente determinar cómo retirar la intención global residual.
