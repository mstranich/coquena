---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
- 047-m3-transferencias
created_at: 2026-10-21T23:26:05.030887800Z
updated_at: 2026-10-21T23:26:05.030887800Z
---

# Resolver backends de servicios globales y propios

## Objetivo

Preparar el backend del único servicio definido por una receta o registrado globalmente, conservando propiedad y selección explícita.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [formato-proyecto.md#backend](../../docs/formato-proyecto.md#backend)
- [servidores.md](../../docs/servidores.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Resolver sólo oficial version/variant o executable externo; backend completo reemplazable por override, sin repositorio configurable ni sustitución automática de familia.
- [ ] Guardar intención y revisiones físicas por ámbito; usar gestionados globales en la home y gestionados propios en el proyecto.
- [ ] Usar ejecutables externos relativos/absolutos in situ y conservar base de origen tras importación, sin copiar bibliotecas ni adoptar propiedad.
- [ ] Reutilizar instalación válida y preparar candidatos separados del activo; diagnosticar hardware incompatible sin bloqueo de integridad inventado.
- [ ] Adaptar backend/runtime y operaciones de recursos al contrato CLI; no confundir selección de backend con alias default de servicio.
- [ ] Preservar pin y variante y la protección del build fijado al cambiar intención; integrar novedades/upgrade en sus entregas específicas.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)
- [047: Aislar transferencias y publicación física por ámbito](../047-m3-transferencias/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin migración automática M2, edición general de YAML, reinicio al seleccionar backend ni proveedores nuevos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
