# Servidores e instancias

## Definición persistida

Un servicio nombrado define backend efectivo y parámetros de proceso/router.
Puede ser global importado o propio de un proyecto. Los consumidores publican
presets en ese servicio sin crear otra instancia. Se admite un proceso activo
por servicio, con composición de sus entradas base y de los consumidores.

Crear/editar no inicia ni cambia ejecución. Service start sobre activo informa
que ya está ejecutándose; up publica el conjunto del consumidor sin reiniciar.
Down retira ese conjunto; service stop detiene el proceso y afecta a todos sus
consumidores. Router compatible puede iniciarse sin modelos cargados e incorporar
presets después; no se exige un preset como condición universal de creación.

## Identificación de backends

Distinguir distribución, familia/variante, versión/build, plataforma y recursos.
Oficial/ggml identifican ggml-org/llama.cpp; bNNNN es release, cuda@13 bibliotecas.
Latest se resuelve según intención/lock, sin sustituir variantes explícitas.

Binarios propios/forks se referencian en proyecto sin imponer flags oficiales.
Familia declarada y versión sondeada no prueban procedencia ni inferencia.
Ejecutable/bibliotecas externos permanecen bajo gestión del usuario.
Nombrar otro origen no lo convierte en proveedor de confianza.
Schema personalizado no implementa API de control del runtime.

## Parámetros del servidor

Defaults del oficial: host 127.0.0.1, port 9931, metrics true; sobrescribibles en YAML,
incluida desactivación explícita de métricas. No imponer esos flags a otros backends.
Ámbitos/emisión en [parámetros](catalogo-de-parametros.md).
Cambiar host/puerto/backend u otra opción no recargable exige reinicio.

## Puertos

Definiciones pueden repetir puerto mientras no lo ocupen simultáneamente.
Comprobar al arrancar: ocupado implica error sin reasignar, sobrescribir configuración
o detener procesos ajenos. No asignar mayor registrado más uno.
Registrar escucha efectiva y coordinar inicios concurrentes para impedir duplicados
por servicio. Clones Git no son una colisión por sí mismos.

## Backend seleccionado ausente

Preparar recurso gestionado faltante usando resolución exacta o cambio explícito
de intención. No sustituir familia automáticamente. Externo ausente exige intervención.
Hardware incompatible produce diagnóstico, sin modificar YAML/lock ni bloqueo
persistente de integridad. Registrar combinación intentada, no éxito.

La preparación se detiene cuando el sondeo detecta incompatibilidad. `--force`
autoriza intentar la preparación conservando la advertencia y la variante elegida;
no acredita ejecución ni omite controles de integridad, archivos requeridos o
plataforma soportada. La autorización se da por invocación, también al reanudar.
Evidencia insuficiente se informa como compatibilidad desconocida y permite
continuar con advertencia; no se convierte en incompatibilidad comprobada.
El sondeo básico contrasta plataforma, driver y capacidades conocidas de los
dispositivos; no demuestra cobertura de arquitecturas del binario ni inferencia.

## Inicio y detención

Validar YAML/esquema y fallos conocidos. No auditar pesos ni recalcular hashes en up.
Preparar faltantes/cambios explícitos sin actualizar otros latest fijados.
Verificar descargas antes de publicarlas y no usar integridad inválida conocida.

Registro local de ejecución conserva ID, identidad verificable (no solo PID), carpeta,
combinación ordenada, configuración efectiva, backend/build, recursos, escucha y log.
INI operativo pertenece al estado del servicio, global o propio, y compone las
entradas de sus consumidores. No es un archivo que el usuario deba redactar.

Interactivo sigue salida/permite decisiones. Con -d, Coquena prepara/inicia/comprueba
y devuelve control; backend sobrevive al cierre sin supervisor Coquena permanente.
TUI/Web usan operaciones daemon: abrir/cerrar no inicia/detiene; acciones explícitas sí.

La ejecución desacoplada del servicio y la interacción de una publicación son
independientes. `service start/restart -d` inicia y comprueba el proceso, que
continúa al terminar Coquena. `up/down` publican o retiran presets de forma
interactiva, también sobre un servicio ya desacoplado, global o propio.
`--start-service` autoriza iniciar el destino detenido durante up; no elimina
la interacción ni acepta el riesgo de interrumpir peticiones al publicar.

Stop solo actúa con identidad verificada. Parada ordenada y espera de diez segundos;
escalar al grupo de procesos si no termina y diagnosticar. En primer plano segunda
interrupción explícita permite forzar. No limpiar registro activo hasta confirmar
terminación; conservar log y recuperación.

Restart prepara/verifica novedades con instancia anterior activa; si falla preparación,
no detenerla. Detener recién con recursos listos, arrancar y comprobar nueva.
Nunca dos instancias simultáneas por servicio.

## Salud y promoción

Health check definido por proyecto/backend, predeterminado para oficial y configurable
para forks. Proceso vivo, acceso API, salud y estado de modelos son observaciones distintas.
Comprobar al arrancar y al consultar, no en un supervisor residente.

En router basta atención de peticiones; no cargar todos los modelos para acreditar
arranque. Preset publicado no garantiza carga/inferencia. Solo promover última buena
local tras salud correcta; preparación de archivos no basta.

La última buena es snapshot de lo probado, no lectura del YAML actual ni éxito importado.
Por defecto solo composiciones respaldadas por YAML guardado son elegibles.
`service.recovery.allow-draft-last-good: true` permite promover borrador aplicado y funcional;
editar/guardar temporal no acredita funcionamiento.

## Aplicación dinámica y memoria

Publicar incorpora la definición del preset a las entradas ofrecidas por el servidor;
no solicita carga por defecto. El campo declarativo
[`warmup`](formato-proyecto.md#receta) solicita precarga explícita al iniciar o al
publicar una entrada nueva o modificada. La carga ante peticiones corresponde al
backend y su configuración, no a otra política de Coquena. Un fallo de precarga se
informa por entrada, distinguiendo publicación aceptada de carga fallida.
La aceptación de presets acredita esa operación de control, no inferencia ni salud
completa; un rechazo puede ser de validación o capacidad, no necesariamente caída.
Falta de respuesta produce diagnóstico de comunicación/plazo, sin inferir terminación.

### Límite entre presets y modelos del router

Up/down y apply/withdraw publican o retiran **presets**. Coquena mantiene su
representación INI y solicita releerla. No son instrucciones de carga o descarga
de modelos y no implementan una política propia de gestión de memoria.

El backend decide ejecución, autoload, límites de modelos simultáneos, sustitución
y tratamiento de peticiones. Coquena no garantiza residencia permanente, memoria
liberada exacta, colocación RAM/VRAM, drenaje ni ausencia de interrupciones. No ofrece
encolado de recargas, drenaje ni forzado como políticas propias.
Un modelo puede volver a cargarse por una petición si autoload está habilitado.
La respuesta a una solicitud y el estado observado se informan por separado.

Al recargar fuentes, el router puede descargar modelos cuyas definiciones cambiaron
o desaparecieron. Es una consecuencia del backend al reconciliar presets; Coquena
no debe traducir up/down a comandos de gestión de modelos. Warmup es
una solicitud explícita adicional, sin garantía de residencia ni carga universal.

Fuentes: [presets y routing oficiales](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#model-presets),
[recarga oficial](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/server-models.cpp).
Estas referencias describen master; la capacidad efectiva se acredita por build.
Apply reconcilia presets seleccionados/prueba sin reiniciar router compatible.
Oficial puede releer INI generado; no asumir API de alta arbitraria de definición.
Entradas cambiadas pueden descargarse y volver a cargarse; no es ajuste gratis
sobre un modelo cargado. No usar recarga mutante para refrescar vistas.

Publicar no dispara una oferta automática de control de modelos. Sin carga comprobada,
informar «publicado, carga no comprobada». La publicación no acredita inferencia.
El estado del modelo se observa sin convertirlo en una operación de control.
Registro activo y referencias reflejan lo aplicado, no YAML leído después.
Cambiar backend exige restart; capacidades de recarga son por build.

## Recuperación y fallos

- Interactivo ofrece reintentar o usar última buena disponible; no decide rollback.
- Daemon recupera última buena en misma invocación ante fallo de nueva preparación/
  arranque, incluso primer fallo. Un candidato conocido como fallido no bloquea servicio.
- Cada invocación daemon intenta una vez la candidata, aunque haya fallado antes.
  Si falla, usa la última buena con warning en terminal y log; no busca otras
  configuraciones históricas más antiguas. Si funciona, la promueve como nueva
  última buena y elimina su marca de fallo. En interactivo, reintentar requiere
  una decisión explícita del usuario.
- Si faltan recursos de última buena, intentar versión exacta, sin resolver latest.
  Si también falla o no existe última buena, informar/registrar y terminar sin bucles.
- Integridad inválida conocida de recurso seleccionado es fatal en ambos modos:
  no reparar/redescargar ni fallback automático. Preservar instancia activa;
  bloquear siguientes inicios que lo incluyan hasta resolver/excluir.
- Incompatibilidad no es bloqueo de integridad ni permite sustituir backend.
- Fallo de apply intenta restaurar composición anterior sin reiniciar router si se
  admite; no presumir transacción/rollback nativos del backend.

UI notifica resultado y ofrece acciones después de recuperación daemon, sin bloquear
el servicio recuperado. No intervenir durante fallback para preguntar.
El hook de fallo de inicio notifica candidata fallida y resultado de recuperación según su contrato; sin configuración no se ejecuta alerta ni se inventa destino.

## Consulta de logs

YAML decide destino incluso externo; por omisión logs del estado del servicio,
en el proyecto o en COQUENA_HOME. Archivo: `<instance-id>.log`.
Rutas relativas desde base. [Consulta/retención](observabilidad.md#logs).
Usuario/DevOps gestiona permisos, no creación automática de cuentas/privilegios.

## Retirar un servidor

El servicio pertenece a su ámbito global o propio. Desregistrar no borra carpeta
ni recursos. No ocultar una instancia activa retirando su registro; detener antes.
Retirada explícita y [limpieza](actualizacion-y-limpieza.md#limpieza) respetan propiedad,
referencias independientes y ejecuciones.

## Directorios gestionados

Pesos, binarios e inventario gestionados pertenecen al ámbito de preparación.
No se exige almacén global a consumidores ni proyectos autónomos.
Externos conservan ubicación/propiedad. Traslados explícitos no sobrescriben recursos
activos, operaciones o datos ajenos; publicación recuperable de rutas/referencias.
No trasladar archivos al cambiar override por inferencia.

## Persistencia

[Definiciones](definiciones-declarativas.md#intención-resolución-y-ejecución) separa
compartible de local: instancia/INI/observaciones no se mezclan con locks portables.
Estado JSON se publica por reemplazo seguro con coordinación de escritores;
log JSONL no obliga a reconstruir catálogos por replay.

### Estructura mínima de JSON acordada

Documentos y protocolo en [persistencia](persistencia-proyectos.md).
No tratar el viejo layout global como contrato de proyecto. Corrupción de registro
no equivale a catálogo vacío ni permite lanzar sustituto sin comprobar.

## Recuperación tras un cierre inesperado

Consultar/actuar verifica existencia e identidad. PID reutilizado no autoriza señales.
Incertidumbre conserva evidencia y bloquea sustitución, no mata procesos ajenos.
Proceso terminado permite limpiar activo, no último log/recuperación.
Falta de salud no significa proceso terminado.
No hay relanzamiento permanente tras terminar Coquena; fallback ocurre en la operación.

## Plazos y control

Health y plazos se describen abajo; códigos/selectores en [comandos](comandos.md).
El backend decide el tratamiento de peticiones durante recarga/retirada. Antes
de cambiar o retirar entradas, la publicación consulta su actividad mediante
fuentes disponibles que no provoquen cargas. Si hay peticiones activas o en cola,
advierte qué entradas pueden verse afectadas y permite aplicar inmediatamente
aceptando el riesgo o cancelar. Si no puede comprobar ocupación, informa que es
desconocida y ofrece la misma decisión. La aceptación se solicita antes de publicar
el INI y pedir recarga; cancelar conserva la composición publicada.

Up/down y apply/withdraw requieren interacción, mediante CLI o una decisión
equivalente en la interfaz visual. Sin interacción disponible se rechaza la
publicación antes de mutar el INI o solicitar recarga. No hay aceptación implícita
por `-d`, `--start-service` o por el modo de inicio del servicio.

La consulta de actividad es una instantánea: no reserva inactividad ni impide que
entre una petición después. La confirmación permite continuar con ese riesgo;
no garantiza ausencia de interrupciones aunque la entrada pareciera libre.
No se ofrecen espera, encolado, drenaje ni un comando que genere deliberadamente
HTTP 500. Los errores de peticiones interrumpidas son consecuencias del backend.
No se reenvía automáticamente una mutación cuyo resultado sea incierto.

## Referencias del adaptador oficial

- [Router y API de modelos](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#using-multiple-models).
- [Implementación de carga y recarga](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/server-models.cpp).

Las referencias master son móviles. La lectura de documentación/código fundamenta
el diseño, pero no acredita ejecución ni compatibilidad de cualquier build.

### Hook de fallo de inicio

`service.server.hooks.start-failed` declara `command` y una lista `args`, sin shell implícito.
Se dispara una vez al fallar la preparación/arranque de la candidata, después del
intento de recuperación, también cuando última buena restaura el servicio. Su
contexto distingue candidata, recuperación y disponibilidad final; un fallo del
hook se diagnostica sin revertir recuperación ni reintentar en bucle. No se exponen
secretos en argumentos o logs compartidos. El contexto se envía como un único JSON por stdin con `format: 1`, `service-id`,
`candidate-id`, `recovery` (not-attempted, succeeded o failed), `available` y
`diagnostic-code`. Sin argumentos expandidos secretos. Plazo del hook: 10 s;
agotarlo no revierte el resultado del servicio.

No detecta inmediatamente caídas posteriores sin observador activo: no hay
supervisor residente. Este hook amplía el aviso de fallo fatal a candidata fallida
con recuperación exitosa; no supone vigilancia permanente.

## Restauración acotada de presets

Si falla una entrada durante o después de apply, se intenta restaurar únicamente
las entradas afectadas a su snapshot publicado anterior; se conservan las demás.
No se supone atomicidad ni rollback nativo del backend. Si restaurar requiere
reiniciar, no se reinicia automáticamente: se informa estado real y se requiere
una operación explícita de reinicio. La aceptación inicial del riesgo de publicar
no autoriza reiniciar automáticamente durante esa restauración.

Una previa experimental puede restaurarse como snapshot operativo anterior sin
promoverla a última buena. El fallo no promueve una nueva última buena ni convierte
la composición parcial en éxito. Un fallo tardío de carga conserva el diagnóstico
de la entrada y no equivale por sí solo a caída del router ni exige cargar todas.
La detección posterior ocurre cuando una operación u observador comprueba el fallo,
sin supervisor permanente.

## Health y resultado de operaciones

`service.server.health` admite `url`, `timeout-seconds` e `interval-ms`.
El oficial usa GET /health del endpoint efectivo, plazo total de inicio 120 s e
intervalo 500 ms; timeout debe ser positivo e interval entre 100 y 10000 ms.
Un fork exige URL explícita o adaptador conocido; no se certifica salud sólo por
estar vivo. HTTP 2xx acredita la prueba configurada; 503 indica no listo y otros
fallos se conservan como diagnóstico. No se hace inferencia para comprobar router.
URL no admite credenciales embebidas; autenticación usa entorno local explícito.

La recarga de presets tiene plazo de observación de 120 s. Agotarlo distingue
solicitud rechazada, aceptada pendiente y envío de resultado incierto. Consultar
no repite la recarga. Una recarga/retirada puede interrumpir peticiones según
el backend: no se prometen transacciones remotas ni drenaje universal.
