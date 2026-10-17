---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 017-configuracion-retencion-logs
- 018-m2-param-define
- 019-m2-config-backends-dir
- 020-m2-config-models-dir
- 021-m2-system-probe
- 022-m2-config-param-reset
- 023-m2-backends-externos
- 024-m2-backend-update
- 025-m2-backend-upgrade
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
- 028-m2-refresh-integridad-local
- 029-m2-emparejamiento-externos
- 030-m2-model-update
- 031-m2-model-upgrade
- 032-m2-transicion-externo-gestionado
- 033-m2-presets-clone-companions
- 034-m2-retirada-recursos
created_at: 2026-10-10T16:36:46.214181900Z
updated_at: 2026-10-13T17:15:42.167209700Z
completed_at: 2026-10-13T17:15:42.167209700Z
transitions:
- status: in-progress
  at: 2026-10-13T15:58:07.400355900Z
- status: complete
  at: 2026-10-13T17:15:42.167209700Z
---
# Validar y preparar el entregable integrado M2

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Preparar el paquete M2 y comprobar cobertura funcional, regresión e integración del recorrido
acordado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Auditar cada comando acordado contra tarjetas/ejecutable; asignar huecos a su entrega
  sin implementar funcionalidades en esta tarjeta.
- [x] Ejecutar regresión M1 y escenarios integrados de
  externos/companions/presets/actualizaciones/retirada/reset/directorios relativos.
- [x] Verificar identidad y recuperación de publicaciones/traslados entre volúmenes y
  protección de externos/recursos en uso.
- [x] Preparar paquete Native AOT win-x64 y ejecutar binario extraído; acordar recursos reales
  antes de descargar pesos y documentar resultados/limitaciones.
- [x] Revisar ayuda, documentos y evidencia de todas las entregas.
- [x] Obtener aprobación explícita del usuario para la aceptación final.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No implementa funcionalidades faltantes: se asignan a su entrega. No incorpora TUI,
HTTP propio, autenticación HF, fuentes de terceros o multiplataforma. No crea pipelines
ni publica releases sin autorización específica.

Excepciones aprobadas por el usuario el 13 de octubre de 2026: corregir la resolución
de rutas de modelos externos y trasladados en consultas/composición de servidores,
con regresiones para las reglas de 026/020. Se encontró al ejecutar el recorrido
integrado; no cambia las reglas canónicas ni amplía formatos/proveedores.
El usuario también indicó que 035 debe implementar `server remove`, registrándolo
como desvío y recuperación/control de daños al concertar el milestone. No se crea
otra tarjeta. La regla ya estaba acordada; la implementación faltó entre las entregas.

## Auditoría y evidencia

Checkout inicial limpio, revisión fuente `faed6daa1f58aad33520112cff2b23ba15f42b7f`.
Dependencias 017–034 completas en el tablero. `leanspec deps 035` informa que no
está migrado al adaptador; se comprobaron metadatos/cuerpos y evidencia manualmente.
La [matriz de cobertura y reproducción](validacion.md) detalla comandos y suites.

### Hallazgos reproducidos

1. **Rutas de principales en servidores:** `ServerManager.ViewLocked` comprobaba
   `LocalPath` con `Home.PathFor`, que rechaza absolutos externos y desconoce la
   base de instalación de gestionados trasladados. El recorrido CLI con principal
   externo rechazó start con `unsafe-path`; la regresión de traslado falló antes
   de corregir producción. Se reutiliza `ModelPaths.File`, ya aplicado en composición,
   sin adoptar archivos externos ni alterar identidades. La prueba enfocada final
   pasó 94 comprobaciones, incluyendo bases relativas/absolutas, consultas CLI,
   composición y bloqueo ante archivos ausentes.
2. **Retirada de servidor sin entrega ejecutable:** la CLI canónica anuncia
   `server [nombre] remove`, pero el dispatcher lo rechaza (exit 2). 011 lo excluyó
   y 034 entregó sólo retirada de backends/modelos/companions. Se detectó en el
   recorrido, después de retirar los recursos. Se consultó su asignación/autorización;
   el usuario autorizó su implementación en 035 como recuperación del milestone.
   Ahora retira sólo la definición detenida, bloquea procesos activos/identidades
   inciertas, limpia el default si corresponde y conserva recursos y logs. No
   inicializa un servidor para retirarlo ni selecciona uno sustituto.

### Recuperación/control de daños: retirada de servidores

La gramática se integra antes de inicializar catálogos. `ServerManager.Remove`
verifica la instancia bajo la misma exclusión de escritores usada por start;
la carrera admite retirar la definición o conservar la ejecución, nunca un proceso
publicado sin su definición. Una ejecución comprobada como terminada no bloquea
la reutilización del nombre; una identidad incierta sí bloquea.

Retirar el predeterminado modifica servers.json y config.json. Dos escrituras
independientes podían dejar una designación rota ante fallo. Se aplica el mecanismo
de snapshot/commit/checkpoint existente en las transacciones del catálogo, con un
marcador dedicado `catalogs/server-removal-transaction.json`: los lectores ven
ambos snapshots publicados y el siguiente escritor completa los checkpoints.
No se modifica el esquema de los documentos principales ni se agrega dependencia.
Antes del commit, el fallo conserva ambos documentos; después, se informa el estado
publicado y pendiente de checkpoint/auditoría sin revertirlo automáticamente.

Este marcador forma parte del estado recuperable del home: cualquier mecanismo
futuro de intercambio/backup deberá usar el estado coherente de LocalStore o
conservar los marcadores pendientes. M3 no se implementa aquí. Los logs conservados
siguen en disco; retirar la definición no agrega una interfaz de consulta histórica
ni reconecta su log al recrear el nombre. El límite de snapshot es 16 MiB, como
en el almacenamiento existente; no se acredita un catálogo cercano a ese límite.

### Documentación y entorno

El inicio rápido y el recorrido M1 usan ahora `system probe` y `preset set` vigentes;
la evidencia histórica M1 conserva las invocaciones originales. La ayuda precisa
los filtros de `param list`. El índice de docs deja de afirmar que no existe
implementación y M2 refleja las tarjetas ya completas; la aceptación integrada
permanece pendiente. No se introducen reglas funcionales nuevas.

El usuario autorizó reutilizar recursos instalados en ${CoquenaHome}, mediante home
separado y servidor temporal en 127.0.0.1:18085, sin descargar pesos. Después aclaró
que C: no debe usarse para pruebas y S: es virtual alojado en D:. Las primeras
suites usaron ${TestRoot} y limpiaron sus fixtures; las
siguientes fijan TEMP/TMP en ${CoquenaHome}-task035-validation/temp. C: no se usa
como destino de las siguientes validaciones. D: es NTFS y S: ReFS con identidades
de volumen distintas; el recorrido D:↔S: no acredita independencia física ni
recuperación ante pérdida del disco anfitrión.

### Validación final — 13 de octubre de 2026

- Suite administrada completa: exit 0. Incluye 94 comprobaciones de servidores,
  23 de retirada de servidores y 86 de ciclo de procesos, además de la regresión
  completa M1/M2. Pruebas enfocadas: `--server-tests` y `--server-removal-tests`,
  exit 0. La prueba de traslado y la retirada fallaron antes de sus correcciones;
  los logs before conservan esos resultados. Al extender la fixture de ejecución
  terminada se corrigieron sus campos/arrays para cumplir el contrato de instancias;
  esos errores de construcción no se atribuyen al producto.
- Producto y runner publicados Native AOT win-x64 con
  `IlcTreatWarningsAsErrors=true`, `TrimmerSingleWarn=false` y
  `SuppressTrimAnalysisWarnings=false`; sin advertencias, exit 0.
- Suite nativa completa contra **el producto extraído del ZIP final**: exit 0,
  stderr vacío. Incluye las 23 comprobaciones de retirada, 86 de procesos,
  236 de instalación/publicación de modelos, 101 de upgrade de modelos y las
  suites de directorios con recuperación entre volúmenes. La prueba de carrera
  start/remove verifica que gana una publicación o la retirada; no queda una
  instancia publicada para una definición retirada.
- Recorrido integrado de CLI: exit 0. Home/evidencia final
  `${CoquenaHome}-task035-validation/integrated-run`.
  Comprueba principal/companion externos, referencia por ID, clon independiente,
  base/comparación de integridad, refresh, dry-run sin escrituras, exclusión de
  externos en update/upgrade, protección activa, referencias rotas y retiradas
  de unidades/backend/servidor con archivos externos intactos.
- Recorrido real del **mismo producto extraído**: exit 0. Home/evidencia
  `${CoquenaHome}-task035-validation/real-run`.
  Gemma y Bonsai responden `Hello!`, finish_reason=stop. Alternancia, ps con
  métricas, logs, restart, stop y retirada de m2-real correctos. Logs preservados,
  catálogos originales con hashes iguales y pesos con tamaño/fecha iguales.
  El servidor temporal está detenido y su definición retirada; los catálogos
  de prueba y recursos externos conservan sus registros para examinar evidencia.
- Documentación: 39 diagramas PlantUML, tres ejemplos JSON y 315 enlaces locales
  de docs/specs/README comprobados. Parser PowerShell valida los cuatro scripts.
  `git diff --check` correcto. Sin nuevas reglas funcionales, paquetes, pipelines,
  tarjetas ni releases. `leanspec tokens`, como deps, no está migrado al adaptador;
  alcance/estado/dependencias/evidencia revisados manualmente.

Logs reproducibles en `artifacts/task035-checks`: managed-tests.txt,
native-tests.txt, native-errors.txt, publish-product.txt, publish-tests.txt,
server-before.txt, server-after.txt, server-removal-before.txt,
server-removal-after.txt, integrated.txt, real.txt y docs.txt. Scripts y comandos
en [validacion.md](validacion.md). Los artefactos locales están ignorados por Git;
esta tarjeta conserva resultados y hashes aunque esos logs se retiren.

### Paquete final preparado

`artifacts/coquena-m2-win-x64.zip`, 5 084 674 bytes. Contiene coquena.exe,
README.md autosuficiente y LICENSE, sin pesos/backend/PDB/runner.
SHA-256 ZIP: `d93d165cafa929874f119b2d7614bd5b1af83a30bd551decd5d581b944b74641`.
SHA-256 ejecutable: `4a2ca79930b268fb5fba8e6d5eca6776e505c7b59e40330a05ea60730f3fa5bd`.
La copia extraída en `artifacts/task035-unpacked` tiene el mismo hash que el
producto publicado; ayuda, suite nativa y ambos recorridos se ejecutaron allí.
La versión comercial y la publicación de un release no se modifican.

La inferencia real es una petición corta por preset, no un benchmark ni una
certificación multimodal. No se volvieron a calcular hashes completos de 22 GB
de pesos reutilizados. Transferencias/actualizaciones remotas y fallos se verifican
con fixtures deterministas; no se hicieron nuevas descargas reales ni upgrades
de instalaciones del usuario. D:↔S: comprueba volúmenes, no discos físicos distintos.

El usuario aprobó explícitamente la entrega el 13 de octubre de 2026 y solicitó
cerrar la tarea y crear un único commit con todo lo aprobado. Se cierra mediante
LeanSpec con código, pruebas, documentación y evidencia en conjunto. No se publica
un release como parte de este cierre.

## Prueba posterior de uso personal

El [recorrido con Ornith y preparación de 0.2.0](prueba-ornith.md) registra la
prueba autorizada el 16 de octubre de 2026, sus incidencias y la evidencia local.
El usuario autorizó después commit, tag, push y release 0.2.0 con binario Windows;
las correcciones 036–039 permanecen abiertas. Las notas de publicación están en
[release-0.2.0.md](release-0.2.0.md).

El [recorrido de aceptación 0.2.1](aceptacion-0.2.1.md) repite la retirada,
reconstrucción y uso real después de las correcciones 036–040. Terminó sin
defectos nuevos y con suite Native AOT exitosa sobre el paquete Windows extraído.
Conserva hashes, límites, procedencia y estado local dejado para uso.
