---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
- 052-m3-evidencia-router
created_at: 2026-10-21T23:26:05.397992100Z
updated_at: 2026-10-28T16:15:31.767331200Z
completed_at: 2026-10-28T16:15:31.767331200Z
transitions:
- status: in-progress
  at: 2026-10-28T13:29:46.884496200Z
- status: complete
  at: 2026-10-28T16:15:31.767331200Z
---
# Registrar servicios y operar su proceso con identidad segura

## Objetivo

Entregar registro, selección y ciclo de servicios globales y propios sin acoplarlos a la publicación de un consumidor.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md](../../docs/servidores.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar service/server list, selección explícita y asignación de default como alias según la gramática canónica; resolver nombre raíz del único service y target self/default/nombrado según contexto.
- [x] Implementar start/stop/restart, foreground/desacoplado y una instancia por servicio; no reasignar puertos ni iniciar sustitutos ante error.
- [x] Verificar proceso/endpoint y registrar snapshot/recursos/log; API inaccesible no prueba terminación ni autoriza actuar sobre un PID reutilizado.
- [x] Preparar antes de detener en restart y conservar activo si preparación falla; validar fallos conocidos sin auditoría completa de pesos por inicio.
- [x] Comprobar salud configurada y promover última buena elegible sólo con evidencia local; respetar plazos y reglas de parada del contrato.
- [x] Implementar baja de servicio detenido conservando archivos/logs; rechazar referencias activas o identidad incierta y retirar alias default sin elegir reemplazo.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)
- [052: Acreditar capacidades del build oficial para presets y observación](../052-m3-evidencia-router/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No implementa up/down ni publica presets de consumidores. La recuperación completa se entrega en 054; no añade supervisor ni servicio del SO.

## Estado y evidencia

Implementación revisada y refactorizada; permanece in-progress hasta revisión y
aprobación explícitas. Sin commit, publicación de presets ni recuperación automática.
No se añadieron paquetes ni se modificaron reglas funcionales.

### Implementación conservada y cambios de revisión

- Se conserva la preparación del proceso separada de los presets: un modelo ausente
  exclusivo de un preset no bloquea start/restart. La preparación local del servicio
  reutiliza también una resolución completa existente o un lock portable recibido,
  sin volver a resolver recursos cuya intención no cambió. No reemplaza el lock completo.
- La CLI separa análisis de argumentos, selección de identidad, preparación y ciclo.
  Sintaxis inválida se rechaza antes de abrir ámbitos. Un start sobre el servicio
  propio ya registrado consulta su identidad antes de leer una intención nueva;
  si está activo informa ese estado aunque el YAML haya sido editado incorrectamente.
- Inicio y reinicio propagan cancelación de consola a preparación y salud. Cancelar
  antes del proceso no lo crea; cancelar durante salud detiene y reconcilia esa
  instancia. Un observador en primer plano comprueba el ID esperado al pedir parada,
  para no detener un reemplazo. El seguimiento limita lectura por vuelta y drena la
  salida final. La baja mantiene el mismo bloqueo del ciclo durante reconciliación
  y retirada del registro.
- Una sola validación de bindings se usa antes de detener y al publicar activo.
  Integridad inválida conocida o instalaciones retiradas bloquean el reinicio antes
  de afectar la instancia anterior. La publicación registra proceso, argumentos,
  directorio efectivo, recursos, combinación, endpoint y log antes de activar el hijo.
- La identidad de Windows incluye PID, creación, ejecutable y pertenencia al job.
  La salud requiere HTTP 2xx configurado y escucha atribuible al grupo. Endpoint
  inaccesible, HTTP 503 o salud ajena no promueven última buena ni borran evidencia.
- La parada comprueba el grupo completo, también si el principal termina antes que
  sus hijos; espera diez segundos y escala al grupo verificado. Informa escalada y
  fallo de señal ordenada. Una colisión de log conserva el archivo y el error Win32
  original: se comprueba cada apertura antes de que otra llamada sobrescriba ese error.
- Documentos activos y última buena se localizan por identidad de servicio y protegen
  recursos en todos los servicios del ámbito. La baja conserva archivos/logs y retira
  el alias default sólo si apuntaba al servicio retirado, sin elegir sustituto.

### Validación ejecutada

Entorno de evidencia pública anonimizado. Estado, fixtures y publicaciones en el
volumen de validación acordado; ubicaciones reales y salidas crudas sólo locales.
Sin descargas de pesos/backends reales ni acreditación de inferencia.

- Build Release de solución y publicación Native AOT win-x64 de producto y runner,
  con advertencias tratadas como errores.
- Servicios: 33 comprobaciones administradas; 35 con el producto publicado, incluyendo
  entrega real de Ctrl+C a su consola durante salud, terminación del backend y código
  130. Incluyen preparación fallida, integridad inválida, contención del ciclo,
  observador obsoleto, cancelación, hijos sobrevivientes, retirada segura, gramática
  inválida sin efectos y recorrido start/restart/list/stop/remove sobre proyecto propio.
- Persistencia: ámbitos, identidad física, copia/traslado, alias default, servicios
  globales, referencias, salud ajena/inaccesible/503, puertos ocupados, colisión de log,
  varias instancias independientes y recuperación entre procesos, en administrado
  y Native AOT. Incluye CLI con default/nombre/self y conservación del proceso cuando
  falla la preparación del reinicio.
- Combinaciones: 69 comprobaciones administradas y Native AOT; se comprueba reutilización
  de resolución completa y lock recibido en preparación exclusiva del proceso.
- Regresiones administradas: recetas 314, argumentos 160, backends 49, modelos 41,
  modelos locales 69 y transferencias 57. Ayuda/aliases y 16 errores sin efectos.
- Regresiones Native AOT con producto publicado: recetas 314, argumentos 160,
  backends 56, modelos 53, modelos locales 83, transferencias 61 y ayuda/aliases.
- Motor de procesos existente: 95 comprobaciones y observabilidad 25, administradas
  y Native AOT. Esta suite usa la API anterior en el runner; la nueva CLI se prueba
  por separado con el producto publicado y no se atribuye a esa suite histórica.
- Siete enlaces locales de la tarjeta, 169 rutas del mapa, espacios finales de los
  archivos nuevos y git diff --check, correctos. Sin cambios en diagramas PlantUML
  ni ejemplos JSON documentales.

### Hallazgos para evaluar

- Si el principal termina espontáneamente y quedan hijos, sin conservar un handle
  verificado previo a esa terminación, la reconciliación conserva la instancia y
  bloquea sustitución/baja. Es una restricción comprobada de disponibilidad para
  preservar identidad; no se actúa sobre PIDs ajenos. La prueba termina explícitamente
  su hijo de fixture y verifica después la reconciliación. El tratamiento operativo
  de ese caso merece evaluación al revisar recuperación, sin asumir autorización
  para matar procesos sólo por su PID.
- Última buena conserva únicamente recursos y ejecución probados localmente. El
  snapshot de intención también contiene presets todavía no publicados: su presencia
  no acredita carga ni inferencia. Recuperación y publicación deberán respetar esa
  distinción; no se implementan aquí las entregas 054/055. Los logs se conservan sin
  política de retención nueva, que corresponde a 061. Las futuras interfaces de M4
  deben usar las mismas operaciones e identidad, sin tomar propiedad del proceso.

### Reproducción

Definir $TestRoot como carpeta externa al repositorio en el volumen acordado:

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet build Coquena.slnx -c Release
dotnet publish src/Coquena -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-service-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --project-persistence-tests
& "$TestRoot/tests/Coquena.Tests.exe" --combination-tests
& "$TestRoot/tests/Coquena.Tests.exe" --runtime-tests
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```

Board/search/view y dependencias revisados manualmente. El comando leanspec deps
continúa sin migrar en el adaptador y no se presenta como validación ejecutada.
No se acredita aceptación integrada de M3 ni se cierra la tarjeta automáticamente.
