# M5 — Chat de prueba e internacionalización

**Estado:** definición en borrador. **Depende de:** [M4](M4-experiencia-completa.md).

## Objetivo y cierre

Probar un preset publicado mediante una conversación breve y observar su respuesta
junto a la telemetría disponible. La CLI y la TUI comparten el mismo destino y
operación; el acceso Chat de En vivo conserva el servicio y preset seleccionados.
Internacionalizar los textos de usuario de la CLI y la TUI, separando su redacción
de la lógica y acreditando el mecanismo con Native AOT.

## Decisión de alcance

El usuario confirmó que la calibración MoE resultó inviable y decidió retirarla.
Se eliminan las antiguas tarjetas 095–099 (antes de renumerar): contenedor Herramientas, viabilidad del port,
metadatos MoE, guardado de recomendaciones e integración del calibrador.
Esta decisión no retira los parámetros de caché MoE ya incorporados en 093.
El estudio conceptual previo no acredita ejecución ni viabilidad; no se dispone
aquí del detalle del experimento para describir su causa técnica.

M5 incluye dos entregas, ambas en estado draft:

- [095: Chat de prueba](../../specs/095-m5-chat-prueba/README.md): vista TUI,
  acceso desde En vivo y CLI.
- [096: Internacionalización](../../specs/096-i18n-textos-de-usuario/README.md):
  textos de usuario traducibles en CLI y TUI, etiquetas de datos y diagnósticos
  persistidos mediante códigos y datos.

El comando pasa de la agrupación anterior a `chat --preset <service>/<preset>`.

## Contrato y aceptación

- Usar una entrada publicada sin iniciar servicios ni publicar implícitamente.
- Contexto durante la sesión, sin historial persistente de conversación.
- CLI interactiva con telemetría debajo de la entrada; salida con /exit o Ctrl+C.
- Métricas con fuente y alcance; correlación de tareas sólo con evidencia.
- Vista previa revisada antes de implementar la presentación TUI.
- Conversación real, tráfico ajeno, ausencia de métricas, cancelación y restauración
  del terminal acreditados con publicación y ejecución Windows Native AOT.

Para i18n, los textos de usuario se centralizan en el mecanismo elegido; las
pruebas conservan códigos y datos como contrato. Se acredita publicación Native AOT
y ejecución en cada idioma entregado, según la tarjeta 096.

Reglas de Chat: [Chat de prueba](../design/benchmarking.md).

## Límites y decisiones abiertas

Sin calibrador, recomendaciones automáticas, plataforma de benchmarking ni
herramientas de agente. Permanecen por definir los límites de contexto, parámetros
de generación, streaming, errores, cambio de destino y entrada/salida redirigidas,
según la tarjeta 095. Para i18n, quedan por definir el mecanismo, la selección de
idioma, los idiomas entregados y el tratamiento del ancho fijo de la TUI, según
la tarjeta 096. La inclusión en M5 no resuelve esas decisiones ni acredita
implementación.

El [estudio histórico](M5-estudio-rendimiento.md) conserva las referencias exploradas.
