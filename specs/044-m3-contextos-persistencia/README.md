---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
created_at: 2026-10-21T23:26:04.646221500Z
updated_at: 2026-10-21T23:26:04.646221500Z
---

# Separar ámbitos y persistir identidad local de recetas y servicios

## Objetivo

Entregar el contexto y la persistencia comunes de proyectos y del ámbito global explícito.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver base/rutas e identidad física; separar home, inventario global y estado local del proyecto según el layout canónico, sin migración implícita de M2.
- [ ] Implementar documentos/versiones, generaciones, exclusión de escritores y publicación recuperable; corrupción nunca equivale a catálogo vacío.
- [ ] Persistir en .coquena/local/ la referencia lógica de receta, nombre usado, entidades creadas y destino; mantener la home como índice de ubicación y ámbito importado.
- [ ] Probar rename de carpeta con conservación de nombres previos, edición atómica del YAML, copia de estado local, alias de rutas y pérdida del índice de home.
- [ ] Proveer registro/consulta de identidad de servicio, consumidor, instancia y recurso, separado de sus nombres y de la combinación; probar aislamiento con nombres repetidos.
- [ ] Hacer recuperables las referencias entre ámbitos y la coordinación con el registro local de recetas; preservar propiedad y evidencia ante identidad incierta.

## Dependencias

Sin bloqueos de implementación dentro de M3. Referenciar documentación no crea una dependencia.

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Infraestructura de contexto y persistencia, no compositor, importación, comandos de ciclo ni deduplicación global.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
