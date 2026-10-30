---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 054-m3-recuperacion
created_at: 2026-10-21T23:26:05.543274100Z
updated_at: 2026-10-30T14:17:14.901196600Z
completed_at: 2026-10-30T14:17:14.901196600Z
transitions:
- status: in-progress
  at: 2026-10-30T13:06:16.075557Z
- status: complete
  at: 2026-10-30T14:17:14.901196600Z
---
# Publicar y retirar presets mediante el INI del servicio target

## Objetivo

Implementar up/down y apply/withdraw como reconciliación de presets, con identidad local y resultado remoto comprobable.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [servidores.md](../../docs/servidores.md)
- [secuencias.md](../../docs/secuencias.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Preparar en el primer up el servicio global predeterminado cuando no está configurado, conforme a la provisión descrita en servidores; reutilizar configuraciones y resoluciones existentes. Distinguir primera instalación de alias incoherente o estado corrupto, sin descargas reales costosas implícitas en las pruebas.
- [x] Acreditar el recorrido de servidor compartido entre proyectos y el reemplazo de la contribución del mismo proyecto al aplicar otra combinación YAML sobre el mismo destino, sin acumular consumidores por ID de combinación ni afectar publicaciones ajenas.
- [x] Validar target exclusivamente como global o self; omitirlo usa global, que resuelve el servicio global designado default. Self selecciona el servicio propio, no elegible como default global. Rechazar nombres y default en el campo target. Up pregunta ante destino detenido y --start-service autoriza sólo ese inicio sin pregunta adicional, sin reiniciar uno activo ni aceptar riesgos de publicación.
- [x] Mantener up/down y apply/withdraw interactivos, independientes del proceso desacoplado: consultar actividad sin cargas, advertir ocupación o incertidumbre en las entradas afectadas y permitir aplicar ahora o cancelar antes de publicar INI. Sin interacción disponible, rechazar sin mutación; no admitir up -d ni encolado/drenaje propios. Seguir la regla de servidores, sin crear una política no interactiva implícita.
- [x] Agregar/actualizar con up y remover con down los presets propios en el INI operativo del target; ambos publican INI y solicitan refresh, conservando entradas ajenas.
- [x] Down usa nombres/destino previos de .coquena/ aunque cambie carpeta o YAML; retirada confirmada permite adoptar name actual en up posterior sin dar de baja el servicio.
- [x] Probar rename, copia de estado, cambio de alias default, colisiones y retiradas inciertas; no apropiarse de publicaciones ajenas ni liberar referencias prematuramente.
- [x] Implementar apply/withdraw individual y atajos con selector de preset; respetar enable, independencia de entidades y warmup sólo explícito, sin gestionar residencia del backend.
- [x] Mantener operación recuperable, revisión de INI y snapshots; no repetir mutaciones inciertas ni asumir transacción remota. Restaurar sólo entradas afectadas sin reinicio implícito.
- [x] Probar INI de dos consumidores, target detenido/inaccesible, fallo parcial/tardío y recursos protegidos hasta confirmar resultado.

## Dependencias

- [054: Recuperar snapshots funcionales sin reinicios implícitos de consumidores](../054-m3-recuperacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin controles de modelos residentes, política propia de ocupación, borrado de pesos o reinicio para completar una retirada. No depende del dashboard ni de observación continua.

## Estado y evidencia

Implementación autorizada y entregada para revisión. La tarjeta permanece
in-progress hasta su cierre de revisión; no se realizó commit ni se agregaron
dependencias. La dependencia 054 está complete y se contrastó con su evidencia.

### Comportamiento entregado

- CLI up/down, preset apply/withdraw y atajos raíz, con contexto antes o después
  del comando, validación previa al aprovisionamiento, target limitado a global/self
  y selección global mediante el alias default. Preparación de presets independiente
  del backend propio que no se está utilizando.
- INI por servicio y contribuciones por propietario, conservación de publicaciones
  ajenas y reconciliación de la combinación aplicada por el mismo proyecto. Estado
  por destino para retirar publicaciones anteriores aunque cambien YAML, carpeta,
  alias o target; down no detiene los servicios.
- Decisiones explícitas de inicio y publicación, consulta sin autoload, cancelación
  anterior al INI y rechazo sin interacción. --start-service no acepta la publicación
  ni reinicia una instancia activa. Apply sobre detenido requiere inicio explícito.
- Operaciones con reservas, INI anterior/candidato, recursos y fases persistidas
  en generaciones coordinadas y manifiestos locales. La reconciliación observa sin
  reenviar una recarga incierta. Un rechazo o fallo de carga permite restauración
  sin reinicio; los fallos parciales conservan las entradas exitosas, incluso las
  del mismo consumidor. La restauración no vuelve a ofrecer candidatas fallidas en
  un bucle. El fallo se diagnostica por entrada.
- Warmup sólo explícito, tras cambios y al iniciar un servicio con contribuciones
  persistidas. Un fallo de precarga de inicio conserva el proceso disponible y no
  promueve última buena. Las publicaciones no promueven última buena del proceso.

### Evidencia ejecutada

Entorno anonimizado. Estado, logs y recursos de validación en el volumen externo
de pruebas; sus rutas reales permanecen fuera del repositorio.

- Producto y runner publicados Native AOT win-x64 con advertencias tratadas como
  errores; publicación final sin advertencias ni errores. Producto ejecutado.
- 41 comprobaciones de publicación en el runner nativo: propietarios, combinación,
  colisiones, cancelación, headless, precarga, restauración parcial y tardía,
  recursos protegidos, revisiones, copia/traslado, corte antes de enviar,
  incertidumbre sin reenvío, destino inaccesible y preparación inicial global.
- Backend real oficial local b11899, commit c6fb3b5be: dos consumidores, cambio de
  definición, retirada, preservación de entradas ajenas y de cambios exitosos del
  mismo consumidor durante una restauración parcial. Fallo real de carga con archivo
  inexistente; no se ejecutó inferencia ni se descargaron modelos.
- CLI sobre ese router desde el runner nativo con decisiones explícitas de interfaz:
  up/down, capas, apply/withdraw, cancelación con código 130, cambio de default,
  edición posterior del YAML, rename, traslado, self, autorización de inicio,
  reutilización de instancia, publicaciones conservadas en dos destinos y retirada
  de los destinos registrados sin detener procesos. Warmup de inicio fallido conserva
  servicio y última buena anterior.
- Producto Native AOT en consola interactiva real: up --start-service, cancelación
  de cambio, publicación de otra combinación y down. Se comprobó por API que cancelar
  preservó el preset, publicar no cargó el modelo y down dejó catálogo vacío con
  health 200. El servicio de prueba se detuvo después mediante una acción explícita.
- Regresión nativa: recetas (323), preparación (69), argumentos (160), recuperación
  (32), servicios (35), backends por ámbito (56), modelos por ámbito (53), archivos
  locales (83), persistencia y ayuda, con la CLI del producto nativo donde corresponde.
  También se ejecutaron las suites administradas pertinentes durante el desarrollo.
- PlantUML de secuencias, enlaces locales, rutas del mapa, privacidad del diff y
  git diff --check comprobados. No se agregaron ejemplos JSON documentales nuevos.

### Reproducción

Definir $TestRoot en el volumen externo acordado y $BackendExe como la instalación
local del build acreditado. El directorio para --publication-router debe estar vacío.

```powershell
New-Item -ItemType Directory -Force "$TestRoot/temp" | Out-Null
$env:TEMP = "$TestRoot/temp"
$env:TMP = $env:TEMP
$env:COQUENA_HOME = "$TestRoot/home"
dotnet publish src/Coquena -c Release -r win-x64 -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/tests/Coquena.Tests.exe" --publication-tests
& "$TestRoot/tests/Coquena.Tests.exe" --publication-router $BackendExe "$TestRoot/router-evidence"
& "$TestRoot/tests/Coquena.Tests.exe" --service-recovery-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-service-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```

### Alcance de revisión y límites

- Las reglas de target global/self y provisión inicial son decisiones funcionales
  acordadas durante esta entrega. La documentación aclara también la composición
  entre proyectos y la diferencia entre conservar resoluciones y acumular publicaciones.
- El primer aprovisionamiento global se probó con selección inyectada, junto a las
  regresiones del motor de backends. No se hizo una descarga real de latest ni se
  acredita un óptimo de hardware, rendimiento o inferencia. Los externos sólo reciben
  el adaptador de publicación al reconocer el build local acreditado; no se generaliza
  esta evidencia a forks arbitrarios.
- El INI y las generaciones conservan composiciones completas conforme al protocolo
  canónico. El coste de escritura y almacenamiento crece con el conjunto de presets;
  permanece el límite de 16 MiB por documento. No se ejecutó un ensayo de escala con
  muchos consumidores. Corresponde evaluar ese volumen para usos intensivos y editores
  de M4; no se creó un registro ni una tarea adicional.
- Lista para revisión y cierre autorizado; sin commit. La evidencia no acredita la
  aceptación integrada de M3, un dashboard ni un supervisor permanente.
