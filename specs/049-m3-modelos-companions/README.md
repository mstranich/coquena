---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
- 047-m3-transferencias
created_at: 2026-10-21T23:26:05.100040400Z
updated_at: 2026-10-21T23:26:05.100040400Z
---

# Resolver inventario y adquisición de modelos y companions por ámbito

## Objetivo

Adaptar selección, adquisición y catálogo de archivos de pesos a recetas y ámbitos, sin gestionar su residencia en el backend.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [descargas.md](../../docs/descargas.md)
- [comandos.md#model](../../docs/comandos.md#model)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver models/companions por claves independientes del name raíz; conservar rol, identidad canónica, commit, cuantización completa y shards como unidad.
- [ ] Adaptar consultas/listados/descargas y retirar recursos propios de forma recuperable, sin inferir propiedad por ubicación ni adoptar externos.
- [ ] Separar descarga explícita protegida de recursos automáticos requeridos; descargar no crea preset ni modifica intención por sí solo.
- [ ] Mantener bindings por identidad y rutas por revisión física; no equiparar un modelo del inventario con una publicación de preset.
- [ ] Exponer progreso conocido sin iniciar transferencias y respetar el camino de cuantización inequívoca entregado por 043 cuando esté disponible; su aceptación integrada corresponde a 068/069.
- [ ] Conservar archivos activos, disponibilidad barata y metadatos; los recursos locales aportados se completan en 050.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)
- [047: Aislar transferencias y publicación física por ámbito](../047-m3-transferencias/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sólo archivos, selección e inventario. Sin comandos para gestionar modelos residentes, biblioteca visual o deduplicación entre proyectos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
