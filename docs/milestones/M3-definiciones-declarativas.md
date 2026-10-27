# M3 — Recetas YAML, servicios globales y proyectos

**Estado:** en implementación, comenzando por la infraestructura de contextos y persistencia. **Depende de:** [M2](M2-gestion-completa.md).

## Objetivo

Usar recetas YAML escritas en un editor externo para definir servicios y consumirlos
desde proyectos, preparar recursos, publicar presets y operar el conjunto mediante CLI.
Tener una interfaz Terminal UI para tener un panel de control visual del comportamiento
de cada servicio junto con sus métricas, slots en uso y sus logs.
La prioridad es ese recorrido ejecutable; init y la edición desde CLI son conveniencias,
no requisitos para recibir, escribir o utilizar una receta, visualizar los servicios y logs.

## Alcance

- Servicios globales en COQUENA_HOME y servicios propios de proyectos; default como
  alias. Configuración e inventario de cada ámbito separados, sin migraciones M2.
- Importación de receta completa o selectiva: import <receta.yaml> --as-default
  --select service,presets,model,params. Oficial gestionado se prepara en el ámbito
  correspondiente; externos se usan in situ sin copiar ni adoptar.
- YAML de servicio, consumidor o combinado; target, name raíz único y como máximo un servicio, warmup, forma corta
  de modelo, overrides ordenados, variables explícitas, resoluciones y estado local.
- Reutilización de archivos locales y matching informativo sin redescarga obligatoria.
- Service start/stop/restart (alias server); backend con alias runtime. Up/down
  publican/retiran el conjunto de la receta actual, sin detener el servicio.
  Up pregunta ante target detenido; --start-service autoriza inicio sin preguntar.
  Up/down y apply/withdraw conservan interacción de publicación y advertencia de
  ocupación/incertidumbre, independiente de service start/restart -d; up no admite -d.
- Preset apply/withdraw individuales; INI operativo por servicio, recarga y protección
  de entradas ajenas. Precarga sólo declarada; aceptación, resultado e incertidumbre
  diferenciados. No se ofrecen políticas de espera/forzado por ocupación.
- Update informativo, upgrade explícito y variante preservada. Pin fijo impide upgrade
  hasta cambio explícito a latest y conserva protección de la versión fijada.
- Verify diagnóstico, limpieza explícita y recuperación local con identidad segura,
  sin auditoría de pesos en cada inicio ni supervisor permanente.
- Logs list/consulta individual/follow, también bajo service/server y atajo raíz:
  continuidad por identidad de servicio entre reinicios/rotación y retención protegida.
- Ps general o por servicio, métricas y eventos sin provocar cargas por observar.
- Corrección de descarga directa para cuantización explícita con una unidad válida;
  ambigüedad conserva selección e integridad/shards siguen siendo obligatorios.

## Contratos y documentación

[Servicios y recetas](../servicios-recetas.md), [formato](../formato-proyecto.md),
[comandos](../comandos.md), [persistencia](../persistencia-proyectos.md) y el
[índice funcional](../README.md) mantienen las reglas.
La propuesta documental no acredita comportamiento del ejecutable.
No se reabren tarjetas históricas M1/M2. La documentación no acredita runtime.
Las referencias a master no prueban compatibilidad universal de forks.
No se adoptan dependencias sin comprobar AOT.

## Secuencia prioritaria

Se abren dos caminos que convergen: el proyecto ejecutable y la observación visual.
044–051 preparan ámbito, lectura y recursos; 053–055 entregan proceso y publicación.
052 acredita temprano INI y telemetría real del build, y 066 inicia la base de terminal
sin esperar actualización, limpieza o edición CLI. Así se comprueban AOT y terminal
antes de acumular el resto de la interfaz.

063–065 construyen muestras, seguimiento y correlación de actividad; 067–069 las
presentan junto a hardware y logs. 070 une estos componentes con un proyecto funcionando
para acreditar el valor central de la entrega. No es una tarjeta opcional ni de M4.
Importación, integridad, actualización, limpieza y conveniencias CLI completan el ciclo
funcional de M3 sin bloquear artificialmente el primer recorrido visual.

073 revisa cobertura y ayuda de comandos efectivamente entregados. 074 requiere 070
y 073 y acredita el paquete integrado con inferencia y dashboard en una terminal real.

## Entregas

Las tarjetas 041–043 se conservan; 044–074 están planificadas. Las dependencias de la tabla coinciden con sus metadatos.

| Tarea | Entrega | Depende de |
| --- | --- | --- |
| [041](../../specs/041-ayuda-cli/README.md) | Organizar la ayuda según la sintaxis real del ejecutable | — |
| [042](../../specs/042-unidades-legibles/README.md) | Unificar tamaños y memoria con escala 1024 y presentación legible | — |
| [043](../../specs/043-descarga-directa/README.md) | Descargar una cuantización explícita inequívoca sin interacción redundante | — |
| [044](../../specs/044-m3-contextos-persistencia/README.md) | Separar ámbitos y persistir identidad local de recetas y servicios | — |
| [045](../../specs/045-m3-lectura-recetas/README.md) | Leer y componer recetas YAML y variables desde archivos manuales | 044 |
| [046](../../specs/046-m3-argumentos-schema/README.md) | Validar argumentos y emitir CLI e INI con schema optativo | 045 |
| [047](../../specs/047-m3-transferencias/README.md) | Aislar transferencias y publicación física por ámbito | 042, 044 |
| [048](../../specs/048-m3-backends/README.md) | Resolver backends de servicios globales y propios | 045, 047 |
| [049](../../specs/049-m3-modelos-companions/README.md) | Resolver inventario y adquisición de modelos y companions por ámbito | 045, 047 |
| [050](../../specs/050-m3-archivos-locales/README.md) | Reutilizar pesos locales y resolver referencias tipadas sin redescarga | 049 |
| [051](../../specs/051-m3-resoluciones-preparacion/README.md) | Persistir combinaciones y preparar recursos con resoluciones reproducibles | 046, 048, 050 |
| [052](../../specs/052-m3-evidencia-router/README.md) | Acreditar capacidades del build oficial para presets y observación | — |
| [053](../../specs/053-m3-servicios/README.md) | Registrar servicios y operar su proceso con identidad segura | 051, 052 |
| [054](../../specs/054-m3-recuperacion/README.md) | Recuperar snapshots funcionales sin reinicios implícitos de consumidores | 053 |
| [055](../../specs/055-m3-publicacion-presets/README.md) | Publicar y retirar presets mediante el INI del servicio target | 054 |
| [056](../../specs/056-m3-importacion/README.md) | Importar recetas completas o selectivas con reemplazo explícito | 055 |
| [057](../../specs/057-m3-verificacion/README.md) | Verificar integridad y mantener diagnósticos locales por recurso | 051 |
| [058](../../specs/058-m3-novedades/README.md) | Consultar novedades y persistir planes exactos por selección | 051 |
| [059](../../specs/059-m3-actualizacion/README.md) | Aplicar planes selectivos con publicación conjunta recuperable | 057, 058 |
| [060](../../specs/060-m3-limpieza/README.md) | Liberar recursos sólo con propiedad y referencias comprobadas | 055, 059 |
| [061](../../specs/061-m3-logs-hooks/README.md) | Configurar logs y notificar fallos de inicio tras recuperación | 054 |
| [062](../../specs/062-m3-seguimiento-logs/README.md) | Seguir logs por identidad de servicio entre reinicios y rotación | 061 |
| [063](../../specs/063-m3-estado-metricas/README.md) | Consultar servicios y hardware con métricas atribuibles y salida finita | 042, 053 |
| [064](../../specs/064-m3-observacion-continua/README.md) | Observar con ps -w o --watch y reconciliar eventos | 063 |
| [065](../../specs/065-m3-actividad-tareas/README.md) | Correlacionar actividad de prompts y generación por entrada, slot y tarea | 052, 064 |
| [066](../../specs/066-m3-tui-base/README.md) | Abrir la interfaz de terminal y navegar entre servicios conocidos | 044, 052 |
| [067](../../specs/067-m3-tui-hardware-graficos/README.md) | Mostrar hardware y gráficas temporales legibles en la terminal | 042, 063, 066 |
| [068](../../specs/068-m3-tui-prompts-tokens/README.md) | Visualizar prompts, generación de tokens y tareas de cada preset | 065, 066 |
| [069](../../specs/069-m3-tui-logs/README.md) | Integrar logs continuos con selección de servicio y navegación | 062, 066 |
| [070](../../specs/070-m3-dashboard-integrado/README.md) | Integrar y acreditar el dashboard Servicios completo | 055, 067, 068, 069 |
| [071](../../specs/071-m3-edicion-cli/README.md) | Persistir cambios puntuales de entidades en el YAML seleccionado | 046, 048, 049 |
| [072](../../specs/072-m3-init/README.md) | Inicializar una receta mínima sin fabricar estado de ejecución | — |
| [073](../../specs/073-m3-integracion-cli/README.md) | Completar cobertura y ayuda de la CLI del milestone | 041, 043, 056, 060, 062, 064, 071, 072, 070 |
| [074](../../specs/074-m3-aceptacion/README.md) | Acreditar y preparar el paquete integrado Native AOT de M3 | 070, 073 |

## Dashboard Servicios

La [pantalla Servicios](../ui-servicio.md) y su [Salt](../tui-servicio.salt.puml)
definen hardware global arriba, actividad por servicio/entrada/slot/tarea en el
centro y logs del servicio seleccionado ocupando el resto. Gráficos temporales
Unicode/braille/ASCII, cifras legibles en tamaño reducido e histórico visual
configurable, sin reservar slots ni alterar inferencia.

Terminal.Gui requiere evaluación Native AOT. Métricas, slots y logs se correlacionan
sólo cuando el build permite atribución; no se inventan tareas ni tasas o progreso.

La pestaña Modelos, sus wireframes de [listado](../tui-modelos.listado.salt.puml) y
[descarga](../tui-modelos.descarga.salt.puml), los editores de Presets/Configuración,
Web UI, benchmarking y calculadora no bloquean M3. No se incorporan acciones de
control al dashboard por aparecer pestañas en el dibujo. [M4](M4-experiencia-completa.md)
define la experiencia visual ampliada.

La TUI y el dashboard son obligatorios en M3. Las tarjetas 065–070 entregan
correlación de tareas, base de terminal, hardware/gráficas, prompts/tokens y logs,
hasta integrarlos en una pantalla con un proyecto funcionando. La aceptación de
074 exige 070: peticiones reales sobre distintos presets/entradas y modelos con
recursos acordados, fases y cifras contrastadas con fuentes, y evidencia visual
del binario publicado en una terminal real. Una pantalla estática no acredita ese recorrido.

## Aceptación y evidencia

- Receta manual válida funciona sin init; init preserva archivos y no fabrica estado.
- Receta global, consumidor y proyecto autónomo completan preparación, ejecución,
  publicación/retirada, actualización, verify, recuperación y limpieza.
- Dos consumidores conservan entradas independientes; down no detiene servicio.
- Import selectivo respeta categorías, default y rutas externas; no inicia servicio.
- Pins, propiedad, revisiones activas y referencias compartidas impiden borrados indebidos.
- Logs continúan entre reinicios y admiten historial; dashboard muestra hardware,
  actividad y logs sin mutar el backend ni atribuir totales GPU a cada servicio.
- Cuantización explícita única descarga sin menú; unidad shardeada sigue siendo única.
- Se prueba recuperación, concurrencia, identidad incierta, falta de métricas, copia
  local, reproducción compatible y pérdida de estado sin importar éxito de otra máquina.
- Cada entrega runtime publica Native AOT win-x64 y ejecuta su binario con evidencia;
  pruebas administradas no lo sustituyen. La tarea [074](../../specs/074-m3-aceptacion/README.md)
  acredita el paquete integrado, incluido el dashboard [070](../../specs/070-m3-dashboard-integrado/README.md).
- Sin release/tag/push ni commits implícitos; revisión explícita antes de cierre.
