# El monitor de descargas puede convertir una finalización exitosa en error

Fecha de registro: 29 de octubre de 2026. Solicitado por el usuario tras evaluar
la coordinación entre monitor y limpieza durante la revisión de
[recuperación de servicios](../specs/054-m3-recuperacion/README.md).

## Problema observado

El monitor de controles de una descarga sigue consultando su directorio mientras
el finalizador puede retirarlo. Una consulta concurrente puede lanzar
FileNotFoundException o DirectoryNotFoundException; al esperar al monitor desde
el finally, el motor puede propagar esa excepción aunque el finalizador haya
terminado correctamente, u ocultar otro diagnóstico.

Se trata de la limpieza del área temporal de una operación, distinta de la
limpieza explícita de instalaciones sin referencias. No se observó pérdida de
recursos publicados ni se acredita corrupción por esta excepción.

## Coordinación actual y causa

1. [DownloadManager.RunAsync](../src/Coquena/Downloads/DownloadManager.cs) inicia
   Monitor antes de transferir y lo mantiene durante el finalizador.
2. [ScopeTransfers.PublishAsync](../src/Coquena/Downloads/ScopeTransfers.cs)
   publica el inventario y llama a Downloads.Cleanup antes de retornar.
   Cleanup retira el directorio de operación. El lease del ejecutor excluye a
   otros ejecutores, pero no a su propio monitor.
3. Control resuelve pause.request/cancel.request mediante
   [ScopeContext.PathFor](../src/Coquena/Projects/ScopeContext.cs). Entre comprobar
   existencia y ejecutar File.GetAttributes, el directorio puede desaparecer.
4. Monitor sólo captura cancelación. RunAsync solicita su parada y lo espera en
   finally, después del finalizador; una tarea de monitor fallida altera el
   resultado de esa salida. También deben considerarse la cancelación y la
   reanudación de operaciones que ya están en fase cleanup.

La publicación coherente y el tratamiento de errores se fundamentan en
[descargas](../docs/descargas.md) y
[persistencia de proyectos](../docs/persistencia-proyectos.md). Este registro no
introduce una política funcional nueva.

## Evidencia y atribución

- Una ejecución Native AOT de la suite de backends falló en
  Monitor/Control/PathFor/GetAttributes al desaparecer el directorio. Su repetición
  pasó las 56 comprobaciones. El síntoma también había sido descrito durante
  [048](../specs/048-m3-backends/README.md), entonces sin atribución demostrada.
- Sobre un export limpio del commit base 98430d3, las 23 ejecuciones ordinarias
  realizadas pasaron 49 comprobaciones cada una. Pasar esas ejecuciones no
  descarta una ventana de concurrencia.
- Un probe externo al repositorio utilizó el contrato público de finalizador de
  DownloadManager con bytes de fixture. Amplió durante tres segundos la ventana
  de desaparición del directorio mediante creación/retirada repetida, validando
  previamente sus rutas dentro de un workspace aislado.
- Esa prueba reprodujo la excepción con el ensamblado base y el actual en
  administrado, y con el ensamblado base en Native AOT. El finalizador había
  completado correctamente; la excepción provenía del monitor al salir de
  RunAsync. No se modificó el código de los ensamblados referenciados.

La condición está demostrada en código anterior a 054. La prueba controlada
reproduce el mecanismo, no una publicación real ni una pérdida de datos, y no
convierte la suite ordinaria en una reproducción determinista. El probe y las
salidas crudas se conservan como evidencia local, fuera de Git.

## Impacto y recomendación por evaluar

**Comprobado:** el motor puede informar fallo después de completar el finalizador.
Esto perjudica la fiabilidad del resultado y del diagnóstico de una preparación;
un error de invocación no demuestra que la instalación no haya sido publicada.

**Recomendación:** establecer una barrera antes de retirar el área: solicitar la
parada del monitor y esperar que haya dejado de leer, manteniendo la exclusión del
ejecutor durante la limpieza. La señal de parada del monitor debe separarse del
token que cancela transferencia/publicación, para no abortar trabajo legítimo.
Evaluar finalización normal, cancelación y reanudación de cleanup, conservando la
atención de controles durante las fases todavía cancelables.

Capturar indiscriminadamente IOException o retirar la comprobación de enlaces no
resuelve la coordinación y puede ocultar errores de seguridad, permisos o estado.
Una tolerancia a desaparición necesitaría un estado terminal propio comprobado.

Una futura corrección debe probar el intercalado de forma controlada, el cese de
lecturas antes de retirar el área, la preservación de diagnósticos y los controles
de pausa/cancelación, en administrado y Native AOT.

## Relación con la planificación

- [047: transferencias y publicación física](../specs/047-m3-transferencias/README.md)
  es el área funcional relacionada y ya está completada. No se reabre automáticamente.
- [054](../specs/054-m3-recuperacion/README.md) conserva la referencia a la evaluación
  que originó este registro; la corrección del motor no se incorpora a su alcance.
- [060: limpieza](../specs/060-m3-limpieza/README.md) enlaza este hallazgo para
  distinguir la limpieza temporal de descargas del clean de instalaciones.
- [074: aceptación integrada](../specs/074-m3-aceptacion/README.md) lo enlaza para
  localizarlo al interpretar las regresiones y la evidencia de entrega.

Sin corrección implementada, tarea correctiva nueva ni dependencia añadida. La
recomendación requiere evaluación y autorización antes de convertirse en trabajo.
