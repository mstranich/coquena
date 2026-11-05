---
status: archived
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 061-m3-logs-hooks
created_at: 2026-10-21T23:26:06.166381400Z
updated_at: 2026-11-04T19:52:46.477068600Z
completed_at: 2026-11-02T17:21:27.358310100Z
transitions:
- status: in-progress
  at: 2026-11-02T15:36:53.259712300Z
- status: complete
  at: 2026-11-02T17:21:27.358310100Z
- status: archived
  at: 2026-11-04T19:52:46.477068600Z
---
# Seguir logs por identidad de servicio entre reinicios y rotación

## Objetivo

Mantener un observador continuo de logs y permitir consulta histórica sin controlar el servicio.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md#logs](../../docs/design/observabilidad.md#logs)
- [comandos.md](../../docs/design/comandos.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar service/server logs list, show por identificador, follow/-f y atajo logs con selección equivalente de servicio/default.
- [x] Seguir identidad de servicio, drenar archivo anterior y continuar en el nuevo tras reinicio/rotación (nuevo archivo por ejecución) con separación visible; no enlazar otra entidad que reutilice nombre.
- [x] Esperar mientras el servicio esté detenido y cancelar sólo el observador; tratar truncado, desaparición y permisos sin bucle intensivo.
- [x] Listar servicio/instancia/fecha/tamaño legible/activo y mantener consulta histórica fija sin convertirla en follow.
- [x] Probar dos reinicios, rotación, escritura concurrente, servicio sin log inicial y retención protegida; exponer lector reutilizable por TUI.

## Dependencias

- [061: Configurar logs y notificar fallos de inicio tras recuperación](../061-m3-logs-hooks/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Decisión de alcance

La rotación de esta entrega es la sucesión de archivos creados por Coquena al
iniciar cada instancia. No se incorpora un mecanismo de rotación por tamaño/tiempo
ni seguimiento de archivos rotados por otros procesos. Truncado, desaparición y
acceso denegado se tratan como errores defensivos del lector.

## Límites

Sin colector residente, cambio de inferencia ni absorber el hook de 061.

## Estado y evidencia

Implementada, validada y cerrada. La dependencia 061 está completa.
El commit de esta entrega se mantiene separado de cualquier release, push o tag.

### Implementación

- ServiceLogReader reutilizable: consulta reciente de hasta 64 KiB, lectura completa
  de un ID histórico hasta su tamaño inicial y seguimiento cancelable por identidad
  de servicio. Los IDs son los de instancia ya registrados por el ciclo de servicio.
- Drena el descriptor anterior antes de abrir la siguiente ejecución registrada,
  incluso cuando aparecen dos reinicios entre consultas. Separa las instancias en
  la salida y espera sin log inicial ni proceso activo.
- Sondeo de 250 ms en reposo; lectura por bloques de memoria acotada y cancelación
  entre bloques. Conserva el decodificador UTF-8 entre escrituras parciales.
- Trata truncado y acceso no disponible, con diagnóstico sin repetición intensiva.
  No sigue un nombre recreado con otro ID ni cambia al cambiar el alias default.
- Superficies equivalentes service/server/root, selección self/default/--service,
  ayuda y limpieza de retención en consulta. Si hay una operación de ciclo activa,
  pospone esa limpieza sin bloquear el seguimiento.
- La documentación de observabilidad aclara la rotación por ejecución y concreta
  la consulta fija de históricos; no se introduce otra política de rotación.

### Validación ejecutada

Entorno anonimizado: Windows win-x64, fixtures locales de salud y salida textual,
sin inferencia ni descargas. Rutas y salidas originales conservadas fuera de Git.
Definir $TestRoot como una carpeta local de pruebas en D: y $Native como el
directorio donde se publica el ejecutable.

~~~powershell
dotnet build tests/Coquena.Tests -c Release
dotnet publish src/Coquena -c Release -r win-x64 -o $Native
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --service-log-follow-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --service-log-hook-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --help-tests "$Native/coquena.exe"
git diff --check
~~~

- Build y publicación Native AOT correctos, sin advertencias ni errores.
- Seguimiento: 47 comprobaciones. Dos reinicios reales del servicio fixture desde
  el binario publicado, drenaje y orden, IDs históricos, escritura concurrente,
  UTF-8 dividido entre sondeos, truncado, desaparición, acceso bloqueado/reintento,
  espera sin primer log, cancelación y rechazo de identidad recreada.
- El proceso observador publicado se cerró sin detener el servicio; cancelación
  cooperativa del lector también comprobada. No se simuló Ctrl+C en consola real.
- Alias server/service y atajo raíz, default, self y nombre reservado mediante
  --service comprobados; consulta histórica fija y reciente acotada comprobadas.
- Regresión logs/hooks: 32 comprobaciones, incluida retención protegida y rechazo
  de retirada de archivos ajenos o sustituidos.
- Ayuda: recorridos administrado y Native AOT, incluidos 16 errores sin efectos.
- Enlaces locales de los documentos modificados y git diff --check correctos.
  No se modificaron diagramas PlantUML ni ejemplos JSON.

### Límites de la evidencia y revisión

No se acredita inferencia real, terminal interactiva, integración TUI ni carga a
escala. La lectura por ID sólo alcanza históricos todavía retenidos; no es un
archivo permanente ni un protocolo de replay durable. No se agregaron paquetes.
El lector reutilizable queda disponible para 069 y las interfaces posteriores,
sin anticipar su integración. No hubo hallazgos adicionales relevantes que
requirieran una decisión de producto.
