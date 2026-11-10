# La publicación de presets no puede ejecutarse sin interacción

Fecha de registro: 5 de noviembre de 2026. Solicitado por el usuario durante la
revisión del manual de usuario ([077](../../specs/077-manual-usuario-y-reorganizacion-docs/README.md)),
que lo considera un defecto de la CLI.

## Problema observado

`up`, `down`, `apply` y `withdraw` terminan con `interaction-required` cuando no hay
una terminal interactiva, aunque el router esté ocioso y no haya nada que confirmar.
`--start-service` no cambia ese resultado. Estos comandos no pueden usarse en scripts
ni en CI. El usuario espera que `-d` evite la solicitud de interacción.

## Implementación y regla vigente

- [PublicationCli](../../src/Coquena/PublicationCli.cs) rechaza la operación si no hay
  interacción disponible, antes de consultar si hace falta una confirmación.
- [Servidores](../design/servidores.md#plazos-y-control) exige interacción y
  declara que no hay aceptación implícita por `-d` o `--start-service`.
- [M3](../milestones/M3-definiciones-declarativas.md) y `coquena --help` indican
  que `up` no admite `-d`.

El comportamiento coincide con la regla vigente. Lo que se cuestiona es la regla, por
lo que una corrección requiere modificar antes la documentación funcional.

## Evidencia

Con el binario Native AOT 0.3.0, `up` y `down` con la entrada redirigida terminaron
con código 3 y `interaction-required`. Los mismos comandos funcionaron en una
pseudo-terminal, sin solicitar confirmación, con un router sin actividad.

## A evaluar

- Qué significa `-d` en estos comandos cuando el router tiene peticiones activas o no
  puede consultarse: aceptar el riesgo, fallar o cancelar. Hoy la regla protege esas
  peticiones de una interrupción no aceptada.
- Si `-d` conviene aquí, dado que en `service start` significa desacoplar el proceso y
  no aceptar un riesgo.

Relacionado con el [hallazgo 004](004-parada-sin-plazo-configurable.md).
