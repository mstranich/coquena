---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 049-m3-modelos-companions
created_at: 2026-10-21T23:26:05.178306100Z
updated_at: 2026-10-27T14:54:15.389872400Z
completed_at: 2026-10-27T14:54:15.389872400Z
transitions:
- status: in-progress
  at: 2026-10-27T14:15:08.701290500Z
- status: complete
  at: 2026-10-27T14:54:15.389872400Z
---
# Reutilizar pesos locales y resolver referencias tipadas sin redescarga

## Objetivo

Usar recursos aportados por el usuario y resolver referencias de argumentos conservando ubicación y propiedad.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Registrar principales/companions y shards locales en el ámbito seleccionado; usar la copia actual, incluso si proviene de otro gestor.
- [x] Resolver model:// y companion:// en args a la ruta de entrada de la unidad, con literal explícito y error por referencia inexistente.
- [x] Resolver también selectores canónicos remotos completos sin prefijo en args de presets, sin exigir declaración de alias; respetar el envoltorio literal. Decisión aprobada por el usuario durante 049; aplicar la regla de formato-proyecto.md y compartir identidad con aliases equivalentes.
- [x] Persistir alta antes del --try-match opcional; fallo remoto, cancelación o falta de interacción conservan registro y uso sin certificar procedencia.
- [x] Conservar externos dentro/fuera de base sin mover, adoptar o borrar bytes; referencias relativas usan la base canónica.
- [x] Mantener transición externo a gestionado sólo explícita, verificada y sin destruir originales o referencias activas.
- [x] Probar copia local, partes incompletas, traslado de proyecto y base de origen de referencias externas durante importación.

## Dependencias

- [049: Resolver inventario y adquisición de modelos y companions por ámbito](../049-m3-modelos-companions/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin escanear otras herramientas, búsqueda visual ni ejecutar la operación de importación global, que corresponde a 056.

## Estado y evidencia

Implementada y aprobada por el usuario para cierre. La entrega y su evidencia se incluyen en un único commit.

### Trabajo realizado y decisiones aprobadas

- Alta externa explícita por CLI en proyecto/global, principales y companions, shards completos o incompletos; observación barata y retirada sin borrar bytes externos.
- Resolución de aliases preparados `model://` y `companion://`, y selectores canónicos completos en argumentos de presets, a una sola ruta de entrada. El envoltorio `literal` conserva texto.
- Matching opcional posterior al alta durable, sin pesos ni hashes, mediante el motor de metadatos existente; conserva el registro ante fallo, cancelación o falta de interacción.
- Transición explícita con snapshot externo en checkpoint, reserva mientras está pendiente y publicación verificada conservando UUID. Actualiza los aliases en la generación que publica la instalación gestionada; nunca modifica los originales.
- Referencias relativas del proyecto conservan la base canónica y sobreviven al traslado; el registro para ámbito global desde otra base guarda rutas absolutas contra el origen. Esto acredita la infraestructura, no la operación de importación de 056.
- El usuario definió que la clave de una declaración local es el nombre del registro: `models.my-model` con `files` equivale a `model add <ruta> --name my-model`, con referencia `model://my-model` en singular. No se adoptó la propuesta de deduplicar distintos nombres locales por rutas. Se preservan UUID internos y colisiones: mismo nombre/unidad reutiliza; otro nombre para los mismos archivos se rechaza. Los remotos conservan aliases por selección canónica.
- La CLI admite nombres locales sin inventar procedencia. Para companions locales, `--type mmproj|mtp` expresa el mismo dato obligatorio que `type` en YAML; un nombre canónico remoto sigue incluyendo `#tipo`. Sin repositorio en el nombre no se intenta matching remoto y se informa la ausencia, conservando el uso local.
- `PrepareLocal` y `PrepareReferenceAsync` entregan la infraestructura consumible por preparación: alta/reutilización local desde la base de la receta y adquisición directa remota sin declaración previa de alias. No se implementan combinación, resoluciones portables, `up`, servicio ni publicación de presets: esa integración permanece en 051 y siguientes.
- Documentación funcional actualizada por una decisión nueva aprobada, no por adaptación silenciosa al código: distinción entre nombre de registro local y alias remoto; equivalencia CLI/YAML; tipo auxiliar local explícito. Persistencia aclara rutas externas y publicación de la transición conforme a las reglas existentes.

### Validación y límites

Evidencia pública anonimizada. Fixtures mínimos GGUF/HTTP; no se descargaron pesos ni se acredita inferencia o rendimiento real. Publicaciones y ejecución de validación conservadas localmente fuera de Git.

- Build Release sin advertencias ni errores. Producto y runner publicados Native AOT win-x64 con `TreatWarningsAsErrors=true`, sin paquetes nuevos.
- 69 comprobaciones nuevas administradas; 83 con runner/producto Native AOT. Incluyen nombres locales y paridad YAML/CLI, companions con tipo explícito, alta, matching, referencias, literals, shards incompletos, traslado, base de origen, transición, reserva, cancelación y rechazo de un registro de origen modificado. Cubren interrupciones inyectadas en las tres etapas de publicación con reanudación; no equivalen a interrupción real del proceso.
- Regresiones Native AOT: argumentos 160, transferencias 61 y backends por ámbito 56, correctas. Modelos por ámbito: 41 administradas y 53 Native AOT, con adquisición directa remota sin alias y reutilización de esa identidad por un alias posterior. Regresiones administradas: recetas 314 y persistencia/identidad con recuperación entre procesos, correctas. Fixtures y estado de la revalidación final en el volumen acordado.
- La comprobación de ayuda detectó una nueva línea superior a 80 columnas; se dividió y se volvieron a publicar producto y runner. Revalidación correcta: ayuda/aliases y 17 errores sin efectos en ambos caminos.
- Enlaces locales y ejemplos JSON de los documentos funcionales modificados, 137 rutas existentes del mapa y `git diff --check`, correctos. No se modificaron diagramas.
- No se presenta la suite general como verde: se ejecutaron los caminos pertinentes indicados. Sin inferencia real, descargas costosas, operación de importación global ni aceptación integrada de M3. La evidencia de archivos y preparación no acredita publicación de presets o ejecución del backend; esos límites permanecen fuera de esta entrega.
- LeanSpec board/search/view y dependencia 049 revisados; `deps` sigue sin migrar al adaptador, por lo que no se le atribuye validación de dependencias.

### Reproducción

Desde la raíz del checkout, con `$TestRoot` definido como carpeta local de validación
en el volumen acordado, fuera de Git:

```powershell
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
dotnet build Coquena.slnx -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-local-model-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-model-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scope-transfer-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-backend-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --argument-tests
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```
