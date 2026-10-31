---
status: complete
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
created_at: 2026-10-21T23:26:05.686014800Z
updated_at: 2026-10-31T13:54:34.269588600Z
completed_at: 2026-10-31T13:54:34.269588600Z
transitions:
- status: in-progress
  at: 2026-10-31T13:28:42.194749400Z
- status: complete
  at: 2026-10-31T13:54:34.269588600Z
---
# Verificar integridad y mantener diagnósticos locales por recurso

## Objetivo

Auditar la selección y exponer fallos conocidos sin convertir cada inicio en lectura completa de pesos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Verificar la receta completa del proyecto y existencia/integridad de backend propio, principales, shards y companions utilizados; no auditar el target global desde el proyecto.
- [x] En `verify --global`, admitir `--select` y `--include-unused`; rechazar ambos flags sin ámbito global y mantener separadas las observaciones de cada ámbito.
- [x] Persistir resultado local por recurso separado de referencias; no cambiar YAML, resoluciones o hashes esperados ni descargar/reparar.
- [x] Distinguir evidencia insuficiente, fallo, indisponibilidad e integridad acreditada; baseline externa explícita es otra acción, no reparación de una discrepancia.
- [x] Resolver un fallo concreto sólo por comprobación satisfactoria o descarga completa verificada; excluir recurso no afirma haberlo reparado.
- [x] Consultar fallos conocidos a bajo costo al iniciar y probar preservación del activo, referencias cruzadas y falta de hash comparable.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin --fix, auditoría implícita por listar/iniciar ni marcar incompatibilidad de hardware como fallo de integridad. No bloquea el primer ciclo de servicios válidos.

## Estado y evidencia

Implementada y validada en Windows win-x64; sin commit. Evidencia con ubicaciones anonimizadas, sin mediciones de rendimiento ni inferencia real.

### Decisiones de alcance confirmadas

- El usuario confirmó que la verificación de proyecto comprueba su receta completa y sus recursos, sin extenderse al backend del target global. `--select` y `--include-unused` son exclusivos de `--global`; esta decisión reemplaza la redacción anterior que no distinguía ámbitos. Se actualizaron comandos y actualización/verificación para expresar la regla del producto.
- Se conserva la distinción canónica entre integridad acreditada, discrepancia, falta de referencia y falta de disponibilidad. Un externo disponible sin referencia comparable no se certifica ni se convierte por esa ausencia en corrupción conocida. La comparación insuficiente conserva el código 6 definido en comandos.

### Implementación

- `VerifyCli` valida la definición sin preparar, descargar ni iniciar; usa las instalaciones de la combinación o identidades concretas del inventario cuando son inequívocas. Una resolución no disponible se informa sin consultar novedades.
- `ResourceVerification` compara SHA-256 completos por archivo y verifica estructura de unidades GGUF; conserva observaciones y fallos concretos en el inventario local. Las referencias y resoluciones conservan su contenido. Aliases de una misma instalación reutilizan la lectura dentro de una auditoría.
- Las bases externas se establecen mediante una acción explícita independiente. No certifican origen ni eliminan una discrepancia previa; las bases de gestionados no se sustituyen.
- Preparación e inicio consultan fallos conocidos sin releer pesos completos. Los servicios compartidos comprueban los ámbitos propietarios de sus contribuciones antes de detener la instancia anterior; la corrupción conocida no habilita fallback. Las contribuciones conservan localmente la ubicación del propietario, comprobada por identidad de ámbito.
- Una descarga explícita completa puede publicar una revisión sana separada; ni proteger una instalación ni iniciar un servicio redescarga automáticamente recursos con fallo conocido.

### Validación ejecutada

- Build de producto y pruebas sin advertencias ni errores; sin dependencias nuevas.
- Publicación Native AOT `Release`, `win-x64`, y ejecución del producto publicado con fixtures y estado en el volumen acordado.
- Verificación: **53 comprobaciones**, incluidas tres ejecuciones del producto Native AOT. Cobertura de YAML inválido sin efectos sobre recursos, resoluciones intactas, ausencia de referencia, baseline explícita, discrepancia, ausencia, lectura bloqueada, cambio durante lectura, cancelación, modelos gestionados, shards, companions, dependencias del backend y redescarga completa.
- Referencias cruzadas: proceso real de fixture con salud HTTP; comprobar fallos de un consumidor conserva el activo y su snapshot, bloquea el reinicio en ambos modos y no invoca recuperación. Un archivo de modelo bloqueado para lectura no impide consultar sus diagnósticos al iniciar el fixture.
- Global: exclusión predeterminada de instalaciones sin uso, selección explícita, inclusión de no usados, rechazo de selector desconocido y separación respecto del proyecto. Producto Native AOT: éxito, discrepancia con código 6 y rechazo de flag global en proyecto con código 2.
- Regresiones: combinaciones **69**, publicación **41**, recuperación **24**, modelos locales **76**, modelos por ámbito **41**, backends por ámbito **49**, servicios **35** (incluido producto publicado).
- Enlaces locales de documentos modificados, referencias del mapa y `git diff --check`, correctos. No se modificaron diagramas PlantUML ni ejemplos JSON.
- Revisión manual mediante board/search/view; la dependencia 051 está completa. `leanspec deps` sigue sin migrar al adaptador y no se presenta como comprobación ejecutada con éxito.

### Consecuencias para revisión y límites

- **Revisiones dañadas conservadas:** una redescarga publica otra instalación sin sobrescribir la anterior ni sanar sus referencias históricas. `--include-unused` puede seguir informando la revisión dañada. Limpieza debe evaluar su retirada con las protecciones canónicas; esta observación no crea una tarea adicional ni adelanta su implementación.
- **Propietario no localizable:** un nuevo inicio de una composición compartida requiere poder consultar los fallos conocidos del ámbito propietario. Si no se demuestra su identidad, se rechaza el inicio y se conserva el activo. La ubicación guardada y el índice son mecanismos de localización, no autoridad para adoptar una copia.
- La lectura completa cuesta E/S proporcional al tamaño auditado. No se midió rendimiento sobre modelos grandes ni escala de muchos consumidores. Las futuras interfaces pueden consumir las mismas observaciones; no se implementa interfaz visual.
- No se ejecutó la suite general ni se acredita inferencia, autenticidad del publicador, salud futura del disco o aceptación integrada de M3. Verify no ejecuta una prueba de carga del modelo.

### Reproducción

Desde la raíz del checkout, definir `$TestRoot` como directorio local de validación externo al repositorio en el volumen acordado:

```powershell
dotnet build tests/Coquena.Tests/Coquena.Tests.csproj --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o "$TestRoot/product"
dotnet run --no-build --project tests/Coquena.Tests -- --verification-tests "$TestRoot/product/coquena.exe"
dotnet run --no-build --project tests/Coquena.Tests -- --combination-tests
dotnet run --no-build --project tests/Coquena.Tests -- --publication-tests
dotnet run --no-build --project tests/Coquena.Tests -- --service-recovery-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-local-model-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-model-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-backend-tests
dotnet run --no-build --project tests/Coquena.Tests -- --scoped-service-tests "$TestRoot/product/coquena.exe"
git diff --check
```
