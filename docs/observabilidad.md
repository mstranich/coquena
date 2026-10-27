# Observabilidad

## Identidad y estado

El registro local aporta proceso, escucha y composición activa; comprobarlos antes
de conectarse. No inferir ejecución desde la selección del editor/YAML ni actuar
sobre PID reutilizado. Salud inaccesible no equivale a proceso terminado.
Un modelo fuera de memoria no implica router caído; distinguir fallo de carga de descarga
voluntaria y conservar código/diagnóstico cuando exista.

Una tarea es una operación distinta de las anteriores, aunque el backend reutilice
su número. Slot, tarea y puerto interno se interpretan dentro del servicio,
instancia del router, entrada y generación de su proceso. Recargar una entrada
puede reemplazar el hijo y reiniciar contadores sin reemplazar el router.
Si no puede comprobarse continuidad, se inicia un tramo de observación separado;
no se fusionan tareas ni se calculan diferencias atravesando ese límite.

Los clientes acceden al puerto del servicio; el router puede reenviar a puertos
internos de procesos de modelo. Un prefijo de puerto en logs sólo se asocia a una
entrada con evidencia contemporánea del adaptador. No prueba PID, propietario o
cliente, ni permite conservar la asociación después de su posible reutilización.

## Instantánea y seguimiento

Ps consulta estado y métricas de la instancia y termina. `ps -w` o `ps --watch` es observador explícito
que permanece y redibuja sin llenar el buffer; TUI/Web observan mientras están abiertas.
Cerrar cualquiera no detiene/reconfigura servidor. Sin supervisor permanente después.

Usar composición activa registrada y entradas realmente publicadas, no presets editados
después del inicio. Errores o métricas desactivadas no ocultan una instancia viva.

## Eventos

Suscribirse a eventos útiles soportados, incluidos cambios originados por clientes
u otras acciones, no solo operaciones propias. En oficial /models/sse informa
estados y progreso; nombres/campos requieren comprobación por build.
No asumir telemetría Prometheus completa, entrega durable, replay o IDs de operación.

Para seguir una carga, suscribir antes de ordenar cuando sea posible y observar
entrada objetivo hasta resultado con plazo acotado. Una consulta puntual puede
reconciliar estado tras desconexión. Una solicitud aceptada no acredita carga final.

No utilizar reload=1 como consulta inocua: puede descargar modelos cambiados.
No cargar modelos para observar métricas. No suscribirse a conversaciones privadas
ni usar APIs internas desaconsejadas como contrato genérico.

## Métricas

Métricas oficiales activadas por defecto, desactivables en YAML. Obtener las que
exponga el build: tasas de prompt/generación, contadores, peticiones activas/esperando
y especulación cuando existan. Nombres/unidades deben reflejar su significado:
una tasa media no es velocidad instantánea exacta.

En router consultar por entrada/modelo, sin mezclar tasas. GET /models identifica
cargados; /metrics con model y autoload=false evita cargas de observación cuando
el backend lo soporte. Eventos complementan consultas: no se ha establecido que SSE
transmita tokens/s, RAM/VRAM o todas las métricas del panel.

Ausente, desactivado, inaccesible o no cargado se muestran como tales; no inventar cero.
Memoria del proceso y memoria global de GPU son distintas.
Consultar no escribe intención ni promueve una composición a última buena sin
la comprobación de salud correspondiente.

### Cifras por tarea, entrada y servicio

El detalle por tarea muestra tokens de prompt procesados y reutilizados de caché,
tokens de salida y tiempos/tasas de esa tarea cuando haya una fuente atribuible.
Tener esos campos en la respuesta de una petición propia no implica que el
observador pueda obtener respuestas de otros clientes. No se inspeccionan sus
conversaciones para completar el panel; un dato sin fuente disponible se indica.

El resumen por entrada/modelo muestra contadores y tasas agregados del proceso que
la atiende, con fuente y período. Las tasas medias del backend conservan su ventana
o bucket y no se presentan como velocidades instantáneas ni se asignan a una tarea.
Los contadores acumulados distinguen tokens procesados, cacheados y generados, y
tiempos de prompt/generación. Un reset o reemplazo del hijo abre otro período.

Un total del servicio sólo suma cantidades compatibles de períodos conocidos y
no solapados, sin contar otra vez la misma actividad por tarea y por entrada.
No se suman tasas medias de entradas como velocidad total instantánea. Sin una
ventana común y una definición acreditada, la tasa total se indica no disponible.
Un total de sesión del observador se identifica como tal, junto con sus huecos;
no se presenta como acumulado completo de la vida del servicio.

Los nombres de campos no bastan para deducir su significado. El adaptador acredita
si un contador de secuencia incluye salida, si los tokens cacheados pertenecen al
prompt actual o al estado final del slot y qué denominador utiliza cada tasa.
No se calcula porcentaje con un total que crece durante generación. El progreso
de logs puede ser discreto o faltar en prompts cortos; ausencia no equivale a cero.
TTFT, ETA y picos requieren fuente/ventana explícita; una primera muestra tardía
no mide el primer token y el máximo de medias no es un pico instantáneo.

## Reconexión

Si se pierde conexión y proceso sigue vivo, mostrar desconexión y reintentar con
esperas crecientes hasta un máximo configurable; seguir mientras observador abierto.
Preferencia GLOBAL del perfil/home del usuario, no YAML de cada servidor.
No reiniciar/detener backend por reconexión. Si termina/cambia identidad, mostrarlo,
sin conectar una observación anterior a otro proceso.
Fórmula, valores y mecanismo de cancelación requieren diseño, sin impedir salir.

## Logs

YAML configura destino de logs, incluso externo; default en el estado del servicio global o propio.
Conservar diagnóstico de fallo, configuración usada y recuperación realizada.
No confundir salida textual, log transaccional, lock o estado de instancia.
Los diagnósticos, logs y capturas destinados a compartirse siguen las reglas de
[privacidad](privacidad.md). La observación local conserva los datos necesarios
para identificar recursos y dispositivos; una salida local no acredita que sea
apta para publicación.

Logs consulta salida reciente; seguimiento se cierra sin detener backend.
Conservar como referencia el último log aunque servidor termine.
Logs list enumera identificador, servicio, instancia, archivo, inicio, tamaño y
condición de activo conocida. Logs show selecciona un histórico concreto y no
cambia de archivo. Logs follow (o -f) sigue identidad del servicio entre reinicios
y rotación: drena lo legible del archivo anterior y continúa en el nuevo, separando
visiblemente ejecuciones. Espera mientras el servicio está detenido; cancelar sólo
cierra el observador. Si se elimina y recrea el nombre con otra identidad, no se
enlaza automáticamente. El acceso por service/server y el atajo raíz son equivalentes.
Retención: límite global de usuario y override de proyecto, independiente de args
del backend. Se conserva default de 100 MiB, configurable en bytes/unidades claras.
No truncar activo/último para cumplir límite; informar exceso de protegidos.
Cambiar límite no borra inmediatamente; limpieza en acciones de ciclo/consulta,
sin recolector residente. No prometer cuota estricta si protegido supera el límite.

Consulta reciente acotada (referencia de 64 KiB) y límites de respuesta/plazo evitan
retener logs o métricas ilimitados. No presentar esas cotas como límites de tamaño
de pesos o de generación de inferencia.

## Notificaciones accionables

Recuperación daemon primero; UI muestra inmediatamente el resultado y acciones por
tecla/clic. No detener el servidor recuperado ni editar YAML por mostrar aviso.
Fallo fatal permite webhook/comando explícito del proyecto. Sin configuración,
solo diagnóstico local. Contrato de payload, secretos y error de alerta requiere diseño;
fallo de notificación no acredita reparación ni éxito del servidor.

## Contratos técnicos

Frecuencia de métricas, render de watch al redirigir salida, historial de gráficos,
snapshot/reconexión de eventos y capacidades por fork requieren especificación.
No convertir observabilidad en benchmark ni en supervisor implícito.

## Consumo de recursos y visión de conjunto

El observador combina telemetría del backend con mediciones del sistema operativo
cuando están disponibles. CPU y RAM se atribuyen a procesos verificados de la
instancia, incluidos procesos de modelos identificables. GPU y VRAM distinguen
mediciones por proceso/dispositivo de totales compartidos; no se atribuye consumo
global de GPU a un servidor concreto sin evidencia. Cada dato indica fuente,
unidad, alcance y disponibilidad; no se inventa cero ni se suman totales de GPU
compartidos varias veces en el resumen.

Las tasas de prompt y generación se muestran por entrada/modelo según lo realmente
expuesto por métricas o logs reconocidos del build. Ventana de medida y valores
acumulados se distinguen de velocidad instantánea; logs sin correlación suficiente
no permiten asignar una tasa a un modelo arbitrario. Falta de soporte, permisos o
métricas se muestra como no disponible con motivo.

La observación general consulta servicios globales y propios de proyectos conocidos,
con presupuestos acotados por instancia. Es agregación de observación, no de
inventarios ni operaciones mutantes. No cambia el alcance de config, recursos,
transferencias o servidor y no introduce un supervisor al cerrar la TUI.
