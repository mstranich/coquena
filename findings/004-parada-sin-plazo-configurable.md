# La parada de un servicio termina forzada y su plazo no es configurable

Fecha de registro: 5 de noviembre de 2026. Solicitado por el usuario durante la
revisión del manual de usuario ([077](../specs/077-manual-usuario-y-reorganizacion-docs/README.md)),
que lo considera un defecto de la CLI.

## Problema observado

Durante el recorrido del manual, `service stop` mostró `[Cierre forzado]` al detener un
servicio global y un servicio de proyecto. Ambos se detuvieron correctamente. La
parada ordenada no concluyó dentro de la espera, y el usuario no puede ajustarla.

## Implementación y regla vigente

- [Servidores](../docs/design/servidores.md) define una parada ordenada con espera de
  diez segundos.
- [ScopedServiceProcess](../src/Coquena/Servers/ScopedServiceProcess.cs) y
  [ServerRuntime](../src/Coquena/Servers/ServerRuntime.cs) fijan esos diez segundos en
  el código. No se encontró un campo YAML, una opción ni un comando `config` para
  cambiarlos.

## Evidencia

Con el binario Native AOT 0.3.0 y el backend oficial `cpu` b12125, el cierre forzado
apareció en dos paradas. No se midió con qué frecuencia ocurre, ni se comprobó por qué
llama-server no terminó con la señal ordenada.

## A evaluar

- Si la señal ordenada llega a llama-server y este la atiende; un plazo configurable no
  resolvería una señal que no surte efecto.
- Si el cierre forzado tiene consecuencias para el usuario, además del mensaje y la
  demora.
- Si el plazo debe configurarse por servicio, de forma global o con una opción de
  `service stop`.

Relacionado con el [hallazgo 003](003-publicacion-sin-interaccion.md).
