# Observabilidad

## Identidad y estado

El registro local aporta proceso, escucha y composición activa; comprobarlos antes
de conectarse. No inferir ejecución desde la selección del editor/YAML ni actuar
sobre PID reutilizado. Salud inaccesible no equivale a proceso terminado.
Un modelo fuera de memoria no implica router caído; distinguir fallo de carga de descarga
voluntaria y conservar código/diagnóstico cuando exista.

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
No guardar credenciales ni URLs firmadas en diagnósticos compartibles.

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
