# Hijos sobrevivientes con identidad incierta bloquean el reemplazo del servicio

Fecha de registro: 29 de octubre de 2026. Solicitado por el usuario durante la
revisión de [recuperación de servicios](../specs/054-m3-recuperacion/README.md).
Antecedente: [ciclo de servicios](../specs/053-m3-servicios/README.md).

## Problema observado

El proceso principal de una instancia puede terminar y dejar hijos vivos en su
grupo de Windows. Si la operación posterior no conserva un handle previamente
verificado del principal, su terminación no permite acreditar de nuevo la
pertenencia al grupo mediante ese proceso.

La reconciliación detecta que el grupo conserva procesos, mantiene el registro
activo y rechaza liberar o sustituir la instancia. En ese estado, una nueva
invocación no puede completar automáticamente la recuperación de disponibilidad.

Esto difiere de una parada que ya dispone de handles verificados: esa operación
puede esperar o terminar el grupo completo según el contrato de parada.

## Implementación y fundamento

[ScopedServiceProcess](../src/Coquena/Servers/ScopedServiceProcess.cs) conserva
la evidencia de instancia cuando
[WindowsServerProcess.RequireGroupEnded](../src/Coquena/Servers/WindowsServerProcess.cs)
detecta miembros vivos. La recuperación respeta ese rechazo y no convierte un PID
o un nombre de grupo en autorización para terminar procesos.

El fundamento es la regla de
[recuperación tras un cierre inesperado](../docs/servidores.md#recuperación-tras-un-cierre-inesperado):
identidad incierta conserva evidencia y bloquea sustitución. El registro de este
hallazgo no modifica esa regla ni autoriza una excepción.

## Evidencia comprobada

[ScopedServiceTests](../tests/Coquena.Tests/ScopedServiceTests.cs) contiene un
fixture cuyo principal termina dejando un hijo vivo. Comprueba que el registro
permanece, que la reconciliación rechaza liberarlo y que sólo puede reconciliarse
después de terminar explícitamente el hijo del fixture. La misma suite acredita
que una parada con handles verificados termina también los descendientes.

La suite pasó en administrado y Native AOT durante la validación de recuperación
(33 y 35 comprobaciones, respectivamente). Son pruebas con procesos locales de
fixture; no acreditan que todo backend real genere o trate hijos de la misma forma.

## Impacto y cuestiones por evaluar

- **Comprobado:** el estado requiere intervención y bloquea otra instancia del
  servicio, aunque el principal ya haya terminado. Se preserva la protección frente
  a señales sobre procesos ajenos.
- **Por evaluar:** qué diagnóstico y recorrido operativo permiten resolver esa
  situación conservando evidencia suficiente de identidad y propiedad. No se ha
  aprobado una política nueva de terminación, adopción o supervisión permanente.
- **Evolución:** las interfaces de [M4](../docs/milestones/M4-experiencia-completa.md)
  deben representar este bloqueo como una condición de intervención, sin reducirlo
  a «servicio detenido» ni convertir la apertura de una interfaz en autorización
  de reinicio. El modo de presentarlo y las acciones que puedan ofrecerse requieren
  evaluación; este registro no anticipa su implementación.

Sin tarjeta nueva, solución comprometida ni cambio de código asociado al registro.
