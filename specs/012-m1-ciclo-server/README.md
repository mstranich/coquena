---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '006'
- '009'
- '011'
created_at: 2026-10-07T22:02:21.184749700Z
updated_at: 2026-10-09T13:55:15.222111100Z
completed_at: 2026-10-09T13:55:15.222111100Z
transitions:
- status: in-progress
  at: 2026-10-09T12:48:36.331279Z
- status: complete
  at: 2026-10-09T13:55:15.222111100Z
---
# Inicio, parada, reinicio e identidad de procesos

## Objetivo
Integrar start foreground/-d, stop por servidor/default o --id y restart con plan/INI y backend concreto. Persistir instancia, capturar salida en texto y coordinar exclusión de inicio/publicación. Implementar recuperación verificando identidad del proceso, sin daemon propio.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [servidores.md](../../docs/servidores.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Proceso fixture y binario nativo prueban Ctrl+C deteniendo foreground, detached sobreviviendo terminal, segundo start sin duplicación, puerto ocupado, PID reutilizado/identidad incierta y caída. Stop limpia registro/INI y conserva log; restart usa configuración guardada y backend instalado vigente, sin red.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación y validación del 9 de octubre de 2026. Sin paquetes nuevos.
El usuario aprobó la entrega, dio por terminada la tarea y autorizó el commit
conjunto de implementación, pruebas, documentación, evidencia y tarjeta 016.
La tarea 012 queda complete; la 016 conserva planned, sin implementación.

### Entrega y decisiones

- `ServerCli` integra start foreground/-d, stop por servidor/default o --id y restart detached, con
  gramática estricta y ayuda actualizada. El segundo start informa la instancia
  existente sin crear otro proceso ni adjuntarse a su salida. Ps, logs/-f y
  retención pertenecen a 013; no se anuncian como disponibles.
- El usuario aceptó solicitar parada ordenada, esperar diez segundos y forzar
  cierre si no termina. Agregó el label de control y doble Ctrl+C: el primero
  confirma recepción, muestra la espera y cómo forzar; el segundo fuerza sin
  esperar el plazo. El callback sólo escribe el aviso y modifica un contador;
  las operaciones y la limpieza ocurren en el bucle de comando.
- `ServerRuntime` compone todos los presets bajo la exclusión de `LocalStore`,
  comprueba disponibilidad de host/puerto sin reasignarlo y genera INI propio.
  Guarda backend/build, escucha, presets y recursos efectivos. Usa un lease
  de archivo por servidor para coordinar start/stop/restart entre procesos;
  la espera de parada no mantiene el bloqueo global del catálogo.
- `WindowsServerProcess` utiliza CreateProcess con consola propia oculta, salida
  estándar/error al mismo archivo de texto y una lista explícita de handles.
  No hay daemon ni lector de pipes que deba sobrevivir para guardar salida.
  El primer plano consulta el archivo con lectura acotada entre comprobaciones
  de interrupción. Los overrides ambientales LLAMA_ARG_* se excluyen del hijo
  para que no sustituyan la definición guardada ni el INI generado.
- El hijo se crea suspendido y asociado atómicamente a dos jobs: preparación
  con kill-on-close y grupo nombrado por GUID. Se publica y sincroniza el
  registro antes de reanudarlo; tras activarlo se deshabilita kill-on-close
  en preparación. El backend hereda el handle del grupo nombrado para que
  permanezca verificable después de terminar la CLI. Ante fallo o caída antes
  de activación, el job de preparación impide dejar un hijo ejecutándose sin
  publicación. Una publicación durable con auditoría fallida conserva el
  registro para recuperación, en lugar de perder la evidencia.
- Identidad: PID + FILETIME de creación + ruta absoluta del ejecutable +
  pertenencia al job de esa instancia. Se conserva el handle del proceso
  durante verificación y acción. PID reutilizado con fecha distinta prueba
  que terminó la ejecución original; se limpia su registro, nunca se mata
  el proceso ajeno. Registros anteriores sin identidad o acceso no verificable
  mantienen incertidumbre y bloquean inicio/parada. Una carrera de salida
  durante inspección sólo permite limpieza si Windows confirma terminación.
- La parada ordenada usa un helper breve del mismo ejecutable: verifica la
  instancia, se adjunta únicamente a su consola y envía CTRL_C_EVENT. Termina
  después de enviar; no administra servidores ni conserva estado propio.
  La política de fuerza termina el job de la instancia, incluidos los hijos
  del router. La limpieza se hace tras confirmar la salida; conserva logs y
  definición. Restart detiene la ejecución anterior y vuelve a componer con
  configuración/build instalado actuales, sin red ni sustitutos.
- La protección de reinstalación de backend reutiliza esta recuperación bajo
  el mismo writer lease: libera registros terminados de identidad verificada;
  conserva la protección de instancias activas o inciertas.
- Documentación canónica y diagramas incorporan la política aceptada. Se
  corrigieron dos saltos de línea de PlantUML ya inválidos en HEAD, en los
  diagramas de reinstalación de actividades/secuencias, sin cambiar su conducta.

### Verificación reproducible

```powershell
dotnet build Coquena.slnx
dotnet run --project tests/Coquena.Tests -- --runtime-tests
dotnet publish src/Coquena -c Release -r win-x64 -o artifacts/task012-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -o artifacts/task012-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task012-tests-native/Coquena.Tests.exe artifacts/task012-native/coquena.exe
& artifacts/task012-tests-native/Coquena.Tests.exe --runtime-tests artifacts/task012-native/coquena.exe
git diff --check
leanspec board
leanspec search ciclo
leanspec view 012
```

Build y publicaciones Native AOT win-x64 con análisis estricto: sin errores ni
advertencias. Suite completa nativa exit 0: CLI 9+9, contratos, 75 comprobaciones
de configuración, 68 de definiciones, 38 iniciales de procesos, almacenamiento,
181 de backends, 54 de descargas, 56 de instalación de backend, 217 de resolución,
168 de selección HF, 127 de GGUF/integridad y 120 de instalación de modelos.

La prueba final de procesos amplía esa cobertura con invocaciones concurrentes
de la CLI y restart con modelo retirado: 42 comprobaciones administradas y nativas.
Incluye eventos reales de consola para uno/dos Ctrl+C, espera de diez segundos,
cierre de hijos, supervivencia tras cerrar consola, dos servidores independientes,
puerto ocupado, PID reutilizado/identidad incierta, caída y fallos de publicación
antes/después del reemplazo durable. Usa homes temporales, fixture de backend
pequeño y GGUF estructural; no modifica D:\Coquena ni descarga pesos.

Verificación local mediante extracción de bloques de docs y la tarjeta en
`artifacts/task012-checks/validate-docs.ps1`: 37 diagramas con PlantUML -checkonly,
3 ejemplos JSON parseados y 104 enlaces locales comprobados. Script y bloques
extraídos son artefactos de verificación locales, no dependencias del producto.
`git diff --check` correcto. Dependencias 006/009/011 completas, revisadas por
board/search/view; no se amplía 013/014 ni M2.

### Ajuste de UX de stop solicitado por el usuario

El 9 de octubre de 2026 el usuario sustituyó el ID posicional por selección
de servidor/default y selección explícita mediante --id, aprovechando la
restricción de una instancia activa por servidor. La gramática y conducta
vigentes se mantienen en [comandos.md](../../docs/comandos.md#servidores-e-instancias)
y [servidores.md](../../docs/servidores.md#inicio-y-detención).

`ServerManager.Actions` reconoce stop como acción del servidor, incluida la
referencia explícita al nombre default. `ServerCli` interpreta ambas formas
antes de consultar entidades; rechaza el antiguo ID posicional, nombre junto
con --id, GUID vacío/inválido y argumentos extra. `ServerRuntime.StopServer`
resuelve la designación actual o el nombre y mantiene el lease de ciclo hasta
la parada. No inicializa ni recrea definiciones. Reutiliza recuperación y
detención por identidad; un servidor detenido recibe diagnóstico de éxito.
La variante --id conserva acceso directo sin depender del default designado.

Validación ampliada: 56 comprobaciones de procesos administradas y con CLI/runner
Native AOT, incluyendo default designado distinto del servidor llamado default,
aislamiento entre dos servidores, nombre explícito, --id sin designación default,
servidor ya detenido, referencias ausentes, identidad incierta y gramática inválida.
Las 68 pruebas de definiciones también pasan. Publicaciones win-x64 estrictas
sin errores/advertencias; suite completa nativa exit 0. Se conservaron las
pruebas de Ctrl+C, cierre forzado, recuperación y publicación anteriores.

Comandos de publicación y ejecución: los de Verificación reproducible arriba.
Validación de documentación: 37 diagramas PlantUML, 3 ejemplos JSON y 107 enlaces
locales correctos. `git diff --check` correcto. Sin nuevos paquetes.

### Prueba manual aportada por el usuario

El 9 de octubre de 2026 el usuario ejecutó:

```powershell
dotnet run --project .\src\Coquena\ -- server start
```

La salida aportada registra la instancia `ff3590fc-4156-4fdb-b4ce-44d4d28991e3`,
PID 47540, servidor default en `127.0.0.1:8080`, modo foreground. El backend real
listó el preset `gemma-4-26B-A4B-it-qat-q4_0` en Router Mode y confirmó escucha.
Al primer Ctrl+C aparecieron el aviso de recepción, el label de detención y
el resultado de parada con log conservado; el backend emitió su mensaje de
limpieza al salir. La salida no muestra cierre forzado.

Evidencia reportada por el usuario, no una nueva ejecución del agente ni una
comprobación independiente de archivos/procesos. Acredita este recorrido
administrado con backend real; no acredita inferencia, doble Ctrl+C manual
ni la ejecución manual del ejecutable Native AOT.

El aviso sobre el futuro puerto 9931 motiva la
[tarjeta 016](../016-alineacion-puerto-inicial-9931/README.md); no cambia
el alcance ni el puerto persistido durante la implementación de 012.

### Límites

La evidencia acredita el ciclo de procesos con fixtures Windows y ejecutables
Native AOT. No acredita inferencia con pesos reales, entregable de 014. Consulta
de métricas/logs, referencia del último log y retención limitada quedan en 013.
El sondeo previo de puerto no reserva la escucha hasta el bind del backend;
salud no se utiliza para declarar muerto un proceso. No hay reinicio automático.

Referencias técnicas consultadas: [CreateProcess y flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags),
[eventos de consola](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent)
y [manejo de interrupción de llama.cpp b11482](https://github.com/ggml-org/llama.cpp/blob/b11482/tools/server/server.cpp).
