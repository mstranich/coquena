---
status: archived
created: 2026-10-07
tags:
- m1
created_at: 2026-10-07T22:02:10.086621600Z
updated_at: 2026-10-28T10:21:41.618438Z
completed_at: 2026-10-08T00:06:19.879828Z
transitions:
- status: in-progress
  at: 2026-10-08T00:05:14.873507800Z
- status: complete
  at: 2026-10-08T00:06:19.879828Z
- status: archived
  at: 2026-10-28T10:21:41.618438Z
---
# Base ejecutable C# y Native AOT

## Objetivo
Crear solución mínima, pruebas y CLI inicial; fijar SDK 10.0.401, net10.0 y win-x64. Elegir sólo dependencias necesarias y comprobar su compatibilidad AOT.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [README.md](../../docs/README.md)

## Trabajo y aceptación
- [x] Publicar y ejecutar el binario nativo; probar ayuda y diagnóstico de comando inválido. No anunciar comandos sin implementar.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Verificado el 7 de octubre de 2026 en Windows x64 con SDK 10.0.401.

### Entrega y decisiones
- Solución `Coquena.slnx`, CLI en `src/Coquena` y pruebas en `tests/Coquena.Tests`.
- `global.json` fija SDK exacto, sin roll-forward ni prereleases; net10.0.
- CLI sin argumentos, `--help`, `-h` y `help`: ayuda por stdout y código 0.
  Comandos/argumentos desconocidos: diagnóstico por stderr, stdout vacío y código 2.
- Sin PackageReference ni framework de comandos/pruebas: BCL y runner ejecutable
  pequeño, suficiente para esta base. El runner se invoca con `dotnet run`, no
  `dotnet test`; opcionalmente recibe el binario publicado y prueba el proceso real.
- PublishAot y TreatWarningsAsErrors activos en la CLI; publicación win-x64
  también exige IlcTreatWarningsAsErrors. `bin`, `obj` y `artifacts` ignorados.

### Comandos reproducibles y resultados
Desde la raíz del repositorio:

```powershell
dotnet run --project tests/Coquena.Tests -c Release
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/publish/win-x64 -p:IlcTreatWarningsAsErrors=true
dotnet run --project tests/Coquena.Tests -c Release -- artifacts/publish/win-x64/coquena.exe
git diff --check
```

- 9 casos administrados OK; publicación nativa exitosa, sin warnings ni errores.
- 9 casos administrados + 9 ejecuciones del binario publicado OK: sin argumentos,
  tres variantes de ayuda, comando inválido, backend aún no implementado,
  --json no soportado y dos combinaciones inválidas de ayuda/argumentos extra.
- Se comprueban códigos de salida, separación stdout/stderr y contenido básico.
- `git diff --check` sin incidencias. JSON y enlaces locales comprobados.
  No se agregaron ni modificaron diagramas PlantUML.

### Límites y revisión
- Sólo base CLI: no instala backends, modelos ni inicia servidores; M1 no está completo.
- No se añadieron paquetes externos; AOT sólo verifica las dependencias de esta entrega.
- `leanspec deps` no está migrado al adaptador; revisión manual de frontmatter:
  001 no tiene depends_on y 002 declara 001. Board/search/view disponibles.
- Sin commits. Siguiente entrega: 002, contratos y fixtures mínimos de M1.
