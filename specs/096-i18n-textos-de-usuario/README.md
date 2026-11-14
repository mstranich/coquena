---
status: draft
created: 2026-11-13
priority: medium
tags:
- i18n cli tui
- m5
created_at: 2026-11-13T16:33:37.861730400Z
updated_at: 2026-11-14T02:15:18.274659300Z
transitions:
- status: draft
  at: 2026-11-13T16:33:49.578233300Z
---
# Internacionalizar los textos de usuario

## Objetivo

Que los textos que muestran la CLI y la TUI puedan traducirse sin tocar la lógica que
los decide, y que las pruebas no dependan de su redacción.
Entrega de [M5](../../docs/milestones/M5-rendimiento.md).

## Situación

- [091](../091-m4-cli-salida-legible-espacio/README.md) creó `src/Coquena/Messages.cs`:
  una clase estática que reúne textos de usuario. El código decide con códigos y
  datos, la clase los convierte en palabras, y las pruebas comparan códigos, datos o
  entradas de la clase.
- La clase sólo contiene los textos que alguna prueba comprobaba y los nuevos de 091.
  La mayoría de los mensajes de la CLI y la TUI sigue escrita en cada archivo.
- Los errores siguen la forma `Error: texto (código).`; el código no se traduce.
- El estado persistido guarda texto ya redactado: `update-plan.json` conserva el
  diagnóstico de compatibilidad como frase, así que un plan viejo sigue mostrando la
  redacción vieja y en el idioma en que se escribió. Para traducir, el estado debería
  guardar códigos y datos, y la redacción hacerse al mostrar.
- Las vistas de datos sin `--json` (`show`, `combination list`, de `TextTree.cs`)
  muestran las claves tal como están en los datos, en inglés (`service-id`,
  `installation-id`). Traducirlas requiere etiquetas por clave.

## Alcance propuesto

- Llevar a `Messages` los textos de usuario que siguen escritos en cada comando y vista.
- Elegir el mecanismo de traducción compatible con Native AOT y el idioma predeterminado.
- Mantener en español neutro los textos actuales.
- Guardar códigos en lugar de frases en el estado persistido (planes de actualización
  y cualquier otro documento que hoy guarde texto para mostrar).
- Etiquetas traducibles para las claves que muestran las vistas de datos.

## Por concretar

- [ ] Mecanismo: clase con tablas por idioma, recursos `.resx` u otro; comprobar AOT antes de adoptarlo.
- [ ] Cómo se elige el idioma (sistema, configuración, variable de entorno).
- [ ] Qué idiomas se entregan.
- [ ] Tratamiento de los textos de la TUI con ancho fijo.

## Criterios de aceptación (borrador)

- [ ] Ningún texto de usuario fuera de `Messages` (o del mecanismo elegido).
- [ ] Las pruebas no comparan frases escritas a mano.
- [ ] Publicación Native AOT y ejecución del binario en cada idioma entregado.
