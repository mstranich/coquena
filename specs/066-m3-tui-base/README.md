---
status: complete
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 044-m3-contextos-persistencia
- 052-m3-evidencia-router
created_at: 2026-10-21T23:26:06.576440300Z
updated_at: 2026-11-06T19:50:43.723596900Z
completed_at: 2026-11-06T19:50:43.723596900Z
transitions:
- status: in-progress
  at: 2026-11-05T23:39:23.944094800Z
- status: complete
  at: 2026-11-06T19:50:43.723596900Z
---
# Entregar el primer monitor TUI de inferencia con gráficas, consumo y logs

## Objetivo

Entregar un monitor utilizable para entender y evaluar una respuesta de llama-server mientras ocurre: fases, avance disponible, rendimiento temporal, consumo y logs.

## Acuerdo de producto y continuidad entre sesiones

M4 es un milestone abierto, centrado en una Terminal UI con buena experiencia de
usuario. Se agregan, corrigen y refinan tarjetas una por una a partir del uso y la
revisión visual. Las tarjetas actuales no definen un inventario cerrado ni su
finalización implica cerrar M4. Cada tarjeta mantiene un alcance concreto; el
horizonte abierto no autoriza implementar otras secciones sin acordarlas.

La primera entrega utilizable es [066](../066-m3-tui-base/README.md): observar
inferencia real con fases, avance disponible hacia la respuesta, curvas de tokens,
promedio/pico, consumo y logs dentro de la terminal. Leer su intención, matriz de
fuentes y aceptación antes de implementar esta tarjeta. Una carcasa de navegación,
hardware aislado o un indicador binario de actividad no satisfacen ese recorrido.

Se comienza por 066; 065, 067, 068 y 069 amplían una experiencia ya utilizable;
070 contrasta su integración. Los IDs/directorios conservan nombres históricos,
no fijan el orden. No esperar a 070 para integrar datos o probar inferencia real.
La Web UI será otra iteración: lo aprendido de UX y los contratos sin widgets
servirán de base, sin implementar ahora web ni copiar literalmente su layout.

## Fuente de verdad

- [Milestone M4](../../docs/milestones/M4-experiencia-completa.md)
- [comandos.md](../../docs/design/comandos.md)
- [interfaces-visuales.md](../../docs/design/interfaces-visuales.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)
- [tui-servicio.salt.puml](../../docs/design/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Evaluar Terminal.Gui v2 como candidata tras comprobar publicación y ejecución Native AOT win-x64 con teclado, Unicode, resize y restauración de terminal; registrar versión y limitaciones antes de adoptarla.
- [x] Exponer la entrada tui definida por comandos; abrir desde un proyecto y navegar por pestañas de servidor, primero globales y después del proyecto, usando registro local sin recorrer discos.
- [ ] Integrar fases, avance disponible, curvas de prompt/generación, promedio/pico, consumo y logs reales; cumplir la matriz y recorrido detallados de esta tarjeta. No dejar estos paneles pendientes de 067–069. Mostrar identidad y selección incluso sin servidor.
- [ ] Resolver foco, navegación por teclado, salida, redimensionado y terminal pequeña conservando información prioritaria; mantener alternativa comprensible sin color.
- [ ] Abrir/cerrar no inicia, detiene, publica ni reconfigura servicios. Cancelar observadores restaura la terminal y libera suscripciones sin afectar backend.
- [ ] Validar la aplicación publicada en una terminal real, incluida salida tras excepción; las pruebas de layout aislado no acreditan esta entrega por sí solas.

## Intención detallada del primer monitor

El usuario necesita evaluar y mejorar presets mientras llama-server responde,
incluso evaluar sucesivos ajustes de una receta editada externamente y aplicada
con `up`, manteniendo el panel abierto o volviéndolo a abrir. Necesita ver algo más que
«el proceso está vivo» o «hay actividad». Sin salir de la TUI debe poder responder:

1. Qué servicio, entrada/preset y modelo están trabajando. Conservar configuración
   activa, identidad de instancia y generación del hijo; no agrupar presets sólo
   porque compartan pesos ni usar el YAML editado como prueba de lo aplicado.
2. En qué fase se encuentra: carga, espera/cola, procesamiento del prompt,
   generación, finalización o fallo, cuando haya evidencia. Distinguir fase
   desconocida, desconexión, dato vencido y ausencia de actividad comprobada.
3. Cuánto avanzó hacia una respuesta: tokens de prompt procesados/cacheados,
   total y porcentaje válidos, duración de fase y tokens de salida disponibles.
4. Cómo rinde: evolución temporal separada de prompt y generación, última tasa,
   promedio y pico observado, con unidades, ventanas y alcance comprensibles.
5. Cuánto consume mientras responde: CPU, RAM, GPU y VRAM disponibles junto con
   la actividad, distinguiendo máquina, proceso y dispositivo compartido.
6. Qué ocurrió: consultar el log del mismo servidor y volver al seguimiento sin
   detener la captura ni perder contexto de las curvas.

### Avance, tiempo restante y «pensando»

«Cuánto falta» es la intención de visibilidad, no una autorización para inventar
la longitud final. En prompt, porcentaje sólo con total estable y semántica
acreditada; diferenciar caché, trabajo real y tiempo. En generación abierta,
mostrar tokens, tiempo transcurrido y ritmo; indicar que el total restante no se
conoce. El máximo configurado de tokens no es una predicción de longitud final.

ETA, TTFT y porcentajes necesitan fuente, fórmula y período comprobados. Una
primera muestra tardía no mide el primer token. No confundir tiempo desde que se
abrió el monitor con latencia de la petición. No deducir «pensando» de silencio:
el razonamiento puede ser generación de tokens. Sólo separarlo de respuesta
visible con señal atribuible, sin interceptar conversaciones ajenas para lograrlo.
Si el backend no distingue la fase, conservar la incertidumbre en pantalla.

## Inventario de APIs y maximización de información útil

Investigar toda la información relevante disponible hoy; no limitarse a dos
campos fáciles ni mostrar un volcado indiscriminado. Distribuir información en
resumen, gráficas y detalle accesible por teclado según su utilidad. Registrar por
campo build/versión/commit, endpoint y parámetros, argumentos requeridos, ejemplo
anonimizado, unidad, alcance, actualización, reset, atribución y disponibilidad.

Contrastar documentación actual y evidencia de 052 con el binario realmente usado.
El [README de llama-server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)
es una referencia cambiante; no acredita capacidades del build local ni autoriza
cambiar el backend para seguir master.

| Fuente candidata | Información a investigar | Precaución y contraste |
| --- | --- | --- |
| GET /models y /models/sse | Entradas, modelos, estados y progreso de carga disponibles | Correlacionar preset publicado; SSE de modelos no se supone stream universal de tokens, fases o métricas |
| /metrics por entrada/modelo en router | Tokens y tiempos de prompt/generación, tasas medias, solicitudes procesándose/en espera, contexto y ocupación útiles | Métricas habilitadas, selección model y consulta sin carga; verificar buckets, hijo y cuándo cambia cada contador |
| /slots | Slot/tarea, actividad, tokens decodificados, parámetros y especulación disponibles | Contrastar campos reales y semántica; is_processing solo no demuestra prompt frente a generación |
| Métricas de especulación | Tokens propuestos/aceptados y medidas derivadas disponibles | Acreditar denominador y período; cero, ausente y desactivado son estados distintos |
| Stream de una petición propia | return_progress, prompt_progress, timings_per_token y cifras finales cuando existan | Opciones y resultados de esa petición, no una API global para observar clientes externos |
| Logs | Fases, avances discretos y diagnósticos complementarios | Parser por build, correlación contemporánea de entrada/tarea; silencio no demuestra inactividad |
| Proveedores de hardware existentes | CPU/RAM/GPU/VRAM y series de sesión | Alcance máquina/proceso/dispositivo, permisos, fuentes ausentes y vigencia |

Comprobar si los contadores cambian durante el trabajo o sólo al finalizar. Una
curva que sólo se actualiza al final no demuestra progreso en vivo: combinar
fuentes acreditadas o declarar la limitación. /models/sse complementa consultas,
no sustituye evidencia de fases. Las peticiones de prueba propias contrastan
valores, pero no autorizan afirmar que su stream está disponible para observar
peticiones externas. El monitor debe servir también para clientes externos.

Mostrar la información útil disponible y la razón de las ausencias relevantes.
No inventar tareas individuales desde un contador agregado de cola. No atribuir
cliente/proyecto por coincidencia de slot, tarea o prefijo. Consulta, reconexión
y navegación no cargan modelos, reinician, publican ni cambian configuración.

## Curvas, promedio y pico

- Separar prompt de generación, detalle por tarea de agregado por entrada y
  recursos globales de consumo atribuible. No copiar una media de entrada en
  cada tarea ni sumar medias como velocidad instantánea total del servicio.
- Presentar promedio y pico cuando sean medibles. El promedio propio identifica
  fórmula y período con tokens/tiempo compatibles; no promediar sin justificar
  tasas de ventanas desiguales. La media del backend conserva su semántica.
- Etiquetar el máximo muestreado como pico observado de esa serie y ventana;
  si son tasas medias del backend, indicar máximo de medias. No presentarlo como
  máximo físico instantáneo. Explicar inclusión de espera, prompt, huecos y caché.
- Calcular deltas sólo con identidad/período y tiempos válidos. Reset o reemplazo
  del hijo abre tramo nuevo, sin deltas negativos o picos espurios. No unir presets
  o instancias diferentes para fabricar continuidad, promedio o mejor rendimiento.
- Mostrar eje temporal, unidad, ventana, fuente y vigencia; detalle accesible
  explica cálculo sin saturar el monitor. Dato ausente no es cero; no interpolar
  huecos como actividad conocida. Refresco visual y muestreo son ritmos distintos.
- Conservar últimos valores de tareas finalizadas, distinguiendo confirmados de
  últimos observados incompletos. Histórico de sesión acotado; el límite de filas
  finalizadas no limita ejecución ni elimina tareas activas o modifica retención.

## Wireframes y revisión de experiencia de usuario

Leer [pantalla Servicios](../../docs/design/ui-servicio.md),
[interfaces visuales](../../docs/design/interfaces-visuales.md),
[monitor con log](../../docs/design/tui-servicio.salt.puml),
[monitor compacto](../../docs/design/tui-servicio-compacto.salt.puml) y
[logs con poca altura](../../docs/design/tui-logs.salt.puml).
Las rutas actuales están en docs/design/. Son borradores y aproximaciones de
intención, no diseños completos o inmutables ni una obligación pixel a pixel.
Completar detalles rutinarios con criterio de UX y documentar decisiones para
revisión. Una decisión nueva que afecte comportamiento requiere consulta cuando
las reglas y la tarjeta no la resuelvan.

Mantener barra común de hardware, pestañas globales antes que las del proyecto,
actividad/rendimiento arriba, hardware compartido y logs en el espacio restante.
Con poca altura, Logs sigue dentro del mismo servicio. Compactar gráficos y
columnas secundarias antes de perder identidad, fase y avance. Las herramientas
futuras dibujadas no se implementan como parte de esta tarjeta ni aparecen vacías.

Evaluar jerarquía legible, foco visible, selección estable durante actualización,
ausencia de parpadeo molesto y navegación/vuelta comprensibles. Las ráfagas de logs
y fuentes lentas no deben congelar teclado ni provocar saltos continuos del layout.
Mantener serie global de hardware al navegar. Revisar con el usuario terminal
grande, compacta, baja, resize y ausencia de color. Una captura bonita o un layout
válido no acredita UX ni una inferencia observable.

## Recorrido mínimo y evidencia de la primera entrega

- [ ] Ejecutar Native AOT win-x64 en terminal real; comprobar candidata visual antes de adoptarla, teclado, Unicode, resize, cancelación y restauración incluso tras excepción.
- [ ] Preparar mediante CLI un servicio y dos presets identificables. Observar peticiones reales en la TUI con prompt, generación, fin, curvas, consumo y log sin esperar a 067–070.
- [ ] Usar un prompt y una generación suficientemente largos para contrastar fases, progreso, tasa, promedio y pico observado. Reutilizar recursos locales acordados; sin descargas costosas implícitas.
- [ ] Observar al menos una petición de cliente externo sin leer su conversación. Contrastar cada cifra con fuente, alcance y momento de actualización; registrar campos no disponibles.
- [ ] Contrastar presets del mismo modelo y distinta configuración; incluir con/sin borrador especulativo cuando lo permitan los recursos acordados. No prometer una mejora por activar especulación; si falta capacidad para el contraste, consultar aceptación afectada y declarar limitación.
- [ ] Probar fuentes ausentes/desactivadas, desconexión, servicio detenido, reinicio/reset y cambio de hijo. No mostrar cero falso, ETA ficticia, fase inventada ni pico por cambio de proceso.
- [ ] Navegar entre servicios, recorrer/pausar log y volver al final conservando identidad, captura y respuesta del teclado. Cerrar la TUI deja el backend atendiendo y conserva receta/publicación.
- [ ] Registrar evidencia visual anonimizada y tabla esperado/observado/fuente de fases, avance, promedio/pico y consumo; incluir fórmulas, ventanas, frecuencia efectiva y límites de atribución.
- [ ] Revisar la experiencia con el usuario y reportar hallazgos de UX/técnicos, no sólo pruebas. Una capacidad esencial ausente exige consulta, no rebajar silenciosamente la aceptación.

## Reutilización y reparto con las ampliaciones

Partir de src/code-map.yaml y verificar los componentes existentes bajo
src/Coquena/: Observation/ServiceWatch.cs, ServiceObservation.cs, ModelEvents.cs,
SystemMetrics.cs y Servers/ServiceLogReader.cs. Reutilizar 062–064, catálogos e
identidad de ámbito; no introducir otra adquisición de hardware por widget.
Estos son puntos de navegación, no imposición de nuevas clases o arquitectura.

066 realiza la correlación y presentación mínimas necesarias para este recorrido.
065 profundiza robustez y cobertura de tareas; 067 amplía hardware/gráficos;
068 amplía detalle, concurrencia e histórico; 069 endurece históricos y navegación
de logs. Ninguna justifica dejar vacío el primer monitor. 070 contrasta escenarios
integrados, sin posponer hasta allí datos reales. La observación de sucesivos
intentos orienta las cifras del monitor; no exige implementar un comparador de
ejecuciones o configuraciones, una plataforma formal de
benchmarking, persistencia histórica, editores o Web UI.

## Dependencias

Además de 044 y 052, esta entrega requiere las capacidades existentes de:

- [062: Seguimiento de logs](../062-m3-seguimiento-logs/README.md).
- [063: Servicios y hardware con métricas](../063-m3-estado-metricas/README.md).
- [064: Observación continua](../064-m3-observacion-continua/README.md).

La CLI local rechaza `leanspec rel add` porque `rel` no está migrado al adaptador.
Estas dependencias se declaran aquí para no omitir el requisito; sus metadatos
no se editaron manualmente. Reconciliar el grafo con la CLI cuando lo soporte.


- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)
- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin editores, Web UI, búsqueda visual ni pestañas vacías presentadas como funciones implementadas. Los controles de ejecución de esta entrega se limitan a iniciar, reiniciar y detener el servicio seleccionado. Reiniciar y detener requieren confirmación explícita. La primera entrega ya acredita observación real; las ampliaciones no excusan paneles vacíos.

## Estado y evidencia

La primera iteración de revisión abre `coquena tui` con Terminal.Gui
2.5.0. Parte de los borradores Salt, toma Strata como referencia estructural y
usa el esquema de color de Coquena que comparten el CSS del sitio y el showcase.
Muestra hardware compartido, presets y tasas medias del servicio enfocado, y
una cola acotada de líneas recientes de log. Cambiar de servicio cambia la
observación visible. El panel de presets conserva el alcance por entrada de las
tasas del backend.

Validado en Windows con publicación Native AOT win-x64 y ejecución en terminal:
hardware del host, catálogo y logs de un router existente, navegación de global
a proyecto con dos servicios de fixture, ampliación de logs con F2 y
restauración de pantalla al salir con Esc. La suite de observación pasó 25
comprobaciones. Evidencia de entorno local, con datos del equipo omitidos.

Segunda iteración de UX: ventana sin marco exterior, hardware en una barra de
estado inferior y selección de servicios mediante la lista nativa. Se retiraron
la leyenda y los atajos propios. El binario Native AOT win-x64 se comprobó en
terminal con flechas para cambiar de servicio, Tab y salida con Esc.

Tercera iteración de UX: campos de ancho fijo en la barra de hardware, series
compactas de 24 muestras de CPU y de la primera GPU en la parte superior, y
pestañas nativas Logs y Presets en el mismo panel. Se verificaron el orden, la
navegación y la salida en terminal con el binario Native AOT win-x64.
En la revisión de color se ajustó el monitor a superficies y texto neutros,
con turquesa como acento, para evitar que los tonos cálidos de las vistas
persistentes se reduzcan a rojo en terminales de 16 colores.

Cuarta iteración visual: Logs y Presets comparten una vista sin bordes y se
seleccionan con botones compactos. La paleta usa roles del sitio y cada muestra
de hardware se colorea por carga. Con color RGB se interpola de turquesa a
dorado y con 16 colores se distinguen dos niveles. Se comprobó en el binario
Native AOT la navegación nativa con Tab, flechas, Enter, Espacio y Esc.
La barra de hardware mantiene las etiquetas atenuadas y aplica la misma escala
a las cifras de ancho fijo. RAM y VRAM se colorean según la fracción de su
capacidad observada, con color neutro cuando falta un total válido.
Los gráficos de CPU y GPU componen un renglón de etiqueta y cifra actual con
un renglón de historial. El componente acepta una cifra formateada y una
relación observado/total, independiente de la unidad. La marca usa fondo base,
y los gráficos y la barra de hardware usan surface.
El selector compacto marca servicio visible, predeterminado y ámbito con
símbolos independientes. Los gráficos muestran CPU y RAM del host y, por
dispositivo detectado, actividad de GPU y VRAM usada/total. El terracota
señala muestras por encima del 90 % de su capacidad.
La barra inferior recupera los pares GPU/VRAM numerados por adaptador. Cada
par conserva su número durante la sesión y pasa al siguiente renglón cuando
falta espacio horizontal.
Sistema comparte el selector superior con los servicios. Presenta el sondeo
de pantalla y de cómputo junto al inventario de telemetría. Los gráficos de GPU
requieren correlación por identidad entre el sondeo CUDA/Vulkan y el adaptador.
La VRAM conserva el número de su GPU; las gráficas de memoria separan la cifra
usada del total atenuado y la barra inferior abrevia GiB como G.
La barra inferior aplica el mismo filtro de cómputo que los gráficos. Se quitó
el encabezado de hardware y la fila de estado incorporó controles de ciclo con
confirmación modal para reinicio y parada.

### Primera evidencia de curvas de inferencia

En el build `b12058-3f8af15a7`, un cliente de prueba externo emitió peticiones
sintéticas con streaming, `return_progress` y `timings_per_token`. Un observador
independiente tomó muestras de `/slots`, `/props` y `/metrics` por entrada con
`autoload=false`. En una prueba larga se registraron 7904 tokens de prompt,
7 cacheados y 768 de salida según el stream propio. No se conservaron textos
de entrada ni de salida en la evidencia publicada.

`/slots` actualizó `n_prompt_tokens_processed` y `next_token[0].n_decoded`
durante las fases correspondientes. `n_prompt_tokens` también cambió durante
generación, por lo que no sirve de denominador. Los acumulados de `/metrics`
cambiaron al concluir cada fase. La TUI publicada mostró prompt y generación
en la prueba real y dibujó tasas y picos de ambas fases a partir de los slots.

Las curvas corresponden a una tarea observada por vez en el preset elegido.
Se usan diferencias de tokens por tiempo monotónico entre muestras de la
misma fase, con media ponderada por tiempo de los intervalos medidos y pico
muestreado. Se cortan los deltas ante huecos, concurrencia ambigua o cambio de
proceso, slot o tarea. Un sondeo sin identidad verificada del hijo o con otra
versión deja el dato sin confirmar. Una simulación local pasó 16 comprobaciones
de fases, media, pico, huecos, concurrencia y reinicios de contadores/identidad.

La entrega se cierra por decisión del usuario con la evidencia descrita arriba.
Las casillas abiertas conservan los criterios que quedaron parcial o totalmente
sin acreditar, incluidos el contraste de dos presets del mismo modelo,
disponibilidad y avance en casos adversos, seguimiento de logs con posición
estable, tamaños de terminal, salida tras excepción y evidencia visual
anonimizada del recorrido integrado.
