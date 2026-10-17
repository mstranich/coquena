---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 044-m3-contextos-persistencia
created_at: 2026-10-21T23:26:04.736157Z
updated_at: 2026-10-21T23:26:04.736157Z
---

# Leer y componer recetas YAML y variables desde archivos manuales

## Objetivo

Obtener intención efectiva y diagnósticos desde recetas escritas externamente, sin inicialización ni editor obligatorios.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar el subconjunto YAML, límites y vocabulario estructural del formato; rechazar claves duplicadas/desconocidas y formas excluidas.
- [ ] Aceptar un único name raíz y como máximo un service singular sin nombre interno; mantener presets, models, companions y arg-defs como secciones independientes.
- [ ] Resolver target omitido/default, self y servicio nombrado; validar requisitos sin iniciar ni crear servicios por leer la intención.
- [ ] Componer archivos/base/overrides ordenados, mapas, listas, null y backend atómico; conservar procedencia por archivo/campo.
- [ ] Interpolar por capa con entorno y archivos env explícitos, respetando prioridad, tipos, valores vacíos, literales y diagnósticos sin exponer secretos.
- [ ] Normalizar modelo escalar a su selección larga y exponer consulta efectiva reutilizable sin red ni preparación; integrar sólo sintaxis CLI respaldada por comandos.md, sin inventar un verbo por analogía con otra herramienta.
- [ ] Evaluar el parser elegido y acreditar Native AOT antes de adoptarlo; probar rutas externas, límites, composición y errores con fixtures.

## Dependencias

- [044: Separar ámbitos y persistir identidad local de recetas y servicios](../044-m3-contextos-persistencia/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin init obligatorio, escritura CLI, editor, resolución remota de pesos ni ejecución de servicios. La validación semántica/emisión de argumentos corresponde a 046.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
