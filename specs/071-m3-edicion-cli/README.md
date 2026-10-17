---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 046-m3-argumentos-schema
- 048-m3-backends
- 049-m3-modelos-companions
created_at: 2026-10-21T23:26:07.173365300Z
updated_at: 2026-10-21T23:26:07.173365300Z
---

# Persistir cambios puntuales de entidades en el YAML seleccionado

## Objetivo

Integrar los comandos de configuración con intención YAML sin construir un editor interactivo ni convertir guardar en aplicar.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Persistir set/bind/unset/clone y definición de entidades en archivo predeterminado o última capa seleccionada, validando antes/después.
- [ ] Preservar comentarios/texto ajenos a la asignación y rechazar edición ambigua o conflicto de bytes/revisión; no reserializar destructivamente toda la receta.
- [ ] Distinguir literal, referencia tipada, null que retira herencia y clon independiente sin copiar archivos.
- [ ] Mantener name único raíz, service singular y presets independientes al editar; cambio de intención no renombra registros ni publica automáticamente.
- [ ] Adaptar param, schema y restablecimiento con impacto visible, sin borrar asignaciones para esconder incompatibilidades.
- [ ] Probar archivos manuales con overrides, edición externa concurrente y ausencia de descargas, refresh o reinicio por guardar.

## Dependencias

- [046: Validar argumentos y emitir CLI e INI con schema optativo](../046-m3-argumentos-schema/README.md)
- [048: Resolver backends de servicios globales y propios](../048-m3-backends/README.md)
- [049: Resolver inventario y adquisición de modelos y companions por ámbito](../049-m3-modelos-companions/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No bloquea lectura/emisión/preparación de recetas manuales. Sin init, editor de texto/formularios, borradores visuales o compatibilidad/migración de M2.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
