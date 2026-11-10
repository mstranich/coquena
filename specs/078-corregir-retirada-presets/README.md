---
status: complete
created: 2026-11-05
priority: medium
created_at: 2026-11-05T18:29:38.138621400Z
updated_at: 2026-11-07T12:48:39.804052700Z
completed_at: 2026-11-07T12:48:39.804052700Z
transitions:
- status: in-progress
  at: 2026-11-05T18:30:07.246048200Z
- status: complete
  at: 2026-11-07T12:48:39.804052700Z
---
# Corregir retirada de presets

## Alcance y reglas

Corrección de down rechazado con destino detenido y withdraw que responde éxito
sin retirar al recibir el nombre publicado completo. Entrega de
[M3](../../docs/milestones/M3-definiciones-declarativas.md), sobre el recorrido
histórico [055](../055-m3-publicacion-presets/README.md).

Reglas canónicas: [comandos](../../docs/design/comandos.md#selectores-de-control),
[servicios compartidos](../../docs/design/servicios-recetas.md#compartir-un-servidor-entre-proyectos)
y [publicación y memoria](../../docs/design/servidores.md#límite-entre-presets-y-modelos-del-router).
Sin dependencias nuevas ni cambios en la política de residencia de modelos.

## Trabajo

- [x] Permitir down sobre destino comprobado detenido mediante el motor existente.
- [x] Resolver withdraw por clave o nombre publicado completo dentro de publicaciones propias.
- [x] Filtrar destinos registrados por coincidencia; rechazar selectores sin coincidencias.
- [x] Conservar nombres anteriores, entradas ajenas y selección sin releer YAML editado.
- [x] Agregar regresiones de CLI con destino detenido y router real.
- [x] Publicar y ejecutar producto y runner Native AOT win-x64.
- [x] Revisar diff, ayuda, enlaces, estado y evidencia.

## Decisiones para revisión

El rechazo de down existía sólo en la CLI; el motor ya admite retirada con el
proceso detenido y mantiene comprobaciones de identidad bajo lease. Se usa ese
camino sin iniciar el servicio ni agregar controles de memoria.

Withdraw anteponía siempre el nombre de receta al selector, incluso si ya era un
nombre publicado completo. Ahora se contrasta contra registros propios, sin usar
el prefijo para atribuir propiedad. Se admite esa forma adicional y se devuelve
unknown-preset (salida 2) si no hay coincidencia, en vez de acreditar una operación
vacía. La ayuda y comandos.md explicitan esta decisión de interfaz. El retiro con
servicio detenido aclara el comportamiento que ya ofrece el motor.

M4 podrá reutilizar estos selectores sin confundir el nombre mostrado por ps con
la clave YAML. No se modifica el protocolo ni su coste de composición completa.

## Evidencia

Entorno de evidencia anonimizado. Producto y runner publicados Native AOT win-x64
con IlcTreatWarningsAsErrors=true y TrimmerSingleWarn=false, sin advertencias ni
errores. Producto ejecutado con --help; runner nativo ejecutado con:

- --publication-tests: 50 comprobaciones correctas; también pasó el runner administrado.
- --publication-router: router real llama.cpp b12058, commit 3f8af15a7. Publicación
  de dos consumidores, retirada por clave y nombre completo tras cambiar name en
  YAML, protección de otras entradas, selectores ajenos/inexistentes rechazados,
  destinos múltiples, down sin detener servicio y down/withdraw tras detenerlo.
  Se comprobó el INI sin reiniciar el proceso para retirar. Sin inferencia ni
  descarga de pesos. El cierre de las instancias de prueba requirió terminar su
  grupo tras agotar el plazo de parada ordenada, comportamiento del ciclo existente.
- --help-tests con el producto publicado: ayuda, alias y 27 errores sin efectos,
  tanto administrado como Native AOT. Ayuda dentro de 80 columnas.
- Enlaces locales de los documentos modificados y git diff --check correctos.
  Sin PlantUML ni ejemplos JSON nuevos. Revisados privacidad y alcance del diff.
- LeanSpec board/search/view ejecutados. Sin dependencias nuevas; la tarjeta 055
  se usa como antecedente, no como bloqueo. deps/tokens no están migrados al
  adaptador local; estado y referencias se revisaron directamente con view.

Reproducción (definir $TestRoot fuera del repositorio y $BackendExe como el build
local compatible; el directorio router-native debe ser nuevo):

```powershell
dotnet publish src/Coquena -c Release -r win-x64 -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/product/coquena.exe" --help
& "$TestRoot/tests/Coquena.Tests.exe" --publication-tests
& "$TestRoot/tests/Coquena.Tests.exe" --publication-router $BackendExe "$TestRoot/router-native"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```

Lista para revisión; tarjeta in-progress hasta cierre de revisión. Sin commit ni
reemplazo de la herramienta instalada. No se operó sobre los servicios/publicaciones
del usuario. Las pruebas de CLI de publicación usan decisiones de interfaz
inyectadas en el runner; no acreditan una sesión interactiva con el producto
instalado ni liberación exacta de RAM/VRAM. Las ubicaciones y salidas crudas se
conservan sólo localmente.

### Consulta de versión

El hotfix también incorpora `coquena --version`. La opción muestra la versión
de compilación, incluido el sufijo de prepublicación, sin el hash de Git.
Se verificó la salida exacta `0.3.0-hf1` en la compilación administrada, en el
ejecutable Native AOT win-x64 y a través de la herramienta global reinstalada
desde paquetes locales. Pasaron las regresiones de ayuda y `git diff --check`.
