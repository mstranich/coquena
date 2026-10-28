---
status: archived
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:43.321536100Z
updated_at: 2026-10-28T10:21:41.652706200Z
completed_at: 2026-10-12T21:52:11.139208400Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.470881300Z
- status: in-progress
  at: 2026-10-12T20:32:38.313669400Z
- status: complete
  at: 2026-10-12T21:52:11.139208400Z
- status: archived
  at: 2026-10-28T10:21:41.652706200Z
---
# Registrar modelos y companions externos

## Objetivo
Incorporar archivos locales al catálogo sin asumir propiedad sobre ellos ni exigir acceso
remoto.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#incorporación-externo-o-gestionado)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar model add y companion add con nombre canónico, ID estable y propiedad
  externa, sin copiar/mover/adoptar archivos.
- [x] Probar archivos y shards estándar, colisiones de nombres/rutas, registro incompleto e
  inaccesible y contradicciones GGUF; no servir unidades incompletas.
- [x] Listar disponibilidad sin red ni hash completo; conservar referencias por ID y
  diagnosticar cambios externos sin reparación.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
Emparejamiento corresponde a 029; refresh/integridad a 028 y retirada a 034.
No copia ni adopta archivos externos.

## Implementación y decisiones

- `model add <ruta.gguf> --name <usuario/repositorio:cuantización>` y
  `model companion add <ruta.gguf> --name <usuario/repositorio#tipo:cuantización>`.
  Tipos iniciales: mmproj y mtp. `--try-match` se rechaza antes del alta en esta entrega;
  su implementación sigue en 029, sin aparentar que se realizó un emparejamiento.
- Catálogo común con GUID, propiedad explícita, tipo de companion y rutas absolutas.
  Los externos no reciben selección remota ni hash implícito; registros gestionados
  anteriores conservan su interpretación por defecto y su resolución de rutas.
- Se aceptan referencias inaccesibles y conjuntos estándar incompletos con diagnóstico.
  Cabeceras inválidas/contradictorias y divisiones no convencionales no crean un registro;
  estas últimas informan la limitación del descubrimiento, sin declarar inválidos sus bytes.
- Nombre y rutas se comprueban bajo la exclusión del home, contra modelos, companions y
  reservas de descarga. El nombre conserva su escritura y las colisiones ignoran mayúsculas.
- Principales y companions tienen listados separados, con disponibilidad, propiedad,
  archivos esperados y avisos de tamaño/fecha. La consulta no modifica las observaciones.
- Presets referencian principales por ID y resuelven sus rutas externas al generar el INI;
  la composición rechaza unidades no disponibles y companions usados como principales.
  Asociar companions a parámetros corresponde a 033.
- El cambio de directorio y su recuperación trasladan sólo unidades gestionadas y
  rechazan destinos que afecten rutas externas registradas. No reparan externos.

## Evidencia — 2026-10-12

- Revisión manual: board/search/view, tarjeta y referencias canónicas; no hay metadatos
  depends-on en esta tarjeta. `leanspec deps` continúa siendo un adaptador no implementado.
- Regresión administrada completa: exit 0; log `artifacts/task026-checks/managed-tests.txt`.
- Publicación estricta Native AOT win-x64 del producto y del runner: exit 0, sin warnings.
  Ejecutables en `artifacts/task026-native/coquena.exe` y
  `artifacts/task026-tests-native/Coquena.Tests.exe`.
- Casos específicos con ambos ejecutables nativos: **44 comprobaciones**, exit 0.
  Incluyen alta/listado CLI persistido, nombres y rutas duplicados, reservas de descarga,
  archivos ausentes/bloqueados, shards ausentes y contradictorios, cambios sin refresh,
  referencias de presets por ID y traslado de un catálogo mixto sin tocar externos.
- Regresión completa con runner y CLI nativos: exit 0, stderr vacío; cubre contratos,
  configuración, parámetros, servidores/procesos, persistencia, descargas, backends,
  resolución HF/GGUF, instalación y traslado de modelos, además de las 44 comprobaciones.
  Logs `artifacts/task026-checks/native-tests.txt` y `native-errors.txt`.
- Documentación: **39 diagramas PlantUML**, **3 ejemplos JSON** y enlaces locales de docs
  válidos; `git diff --check` sin errores. Referencias locales de esta tarjeta revisadas.
- Se usaron archivos pequeños y homes temporales; no se descargaron pesos reales ni se
  ejecutó inferencia. No se certifica compatibilidad de carga de los modelos externos.

Comandos ejecutados desde la raíz:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --external-model-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet restore tests/Coquena.Tests/Coquena.Tests.csproj -r win-x64 -p:PublishAot=true
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task026-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task026-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task026-checks/run-native.ps1 -Filter '--external-model-tests' -OutputName 'external-native'
& artifacts/task026-checks/run-native.ps1 -OutputName 'native'
& artifacts/task026-checks/validate-docs.ps1
git diff --check
```

El script local de ejecución conserva el entorno y normaliza Path/PATH antes de iniciar
el runner. Los logs de publicación y scripts están en `artifacts/task026-checks/`.
El primer intento de publicar el runner con `--no-restore` diagnosticó NETSDK1047 por
faltar el destino win-x64 en assets; se restauró ese destino sin agregar dependencias
y ambas publicaciones finales pasaron.

Implementación y evidencia aprobadas por el usuario. Se cierra la tarjeta y se entrega
código, pruebas, documentación y evidencia en un único commit.
