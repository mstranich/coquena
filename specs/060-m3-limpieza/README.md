---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 055-m3-publicacion-presets
- 059-m3-actualizacion
created_at: 2026-10-21T23:26:05.923210500Z
updated_at: 2026-10-21T23:26:05.923210500Z
---

# Liberar recursos sólo con propiedad y referencias comprobadas

## Objetivo

Entregar limpieza explícita y gestión de protecciones sin borrar recursos ajenos o referenciados.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md#limpieza](../../docs/actualizacion-y-limpieza.md#limpieza)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar promoción a explícito y desprotección sin transferencia/borrado; conservar pins, activo, identidad incierta, última buena, combinaciones y operaciones.
- [ ] Calcular elegibles y espacio con referencias cruzadas globales/proyectos bajo coordinación; raíz inaccesible no autoriza inferir ausencia de uso.
- [ ] Implementar clean con preview, confirmación y --dry-run/--yes según gramática; revalidar revisión antes de eliminar y mostrar espacio legible realmente liberado.
- [ ] Implementar listado/olvido de combinaciones conforme al contrato, sin borrar YAML/pesos, publicaciones activas o referencias independientes.
- [ ] Retirar la retención automática de builds como política de limpieza; una edición o upgrade no autoriza borrar por inferencia.
- [ ] Probar archivo externo interior, vecinos añadidos, eliminación parcial/reintento y contribuciones retiradas de un servicio compartido.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)
- [059: Aplicar planes selectivos con publicación conjunta recuperable](../059-m3-actualizacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Hallazgos relacionados

- [002: Coordinación entre monitor de descargas y limpieza](../../findings/002-coordinacion-monitor-limpieza.md).
  La retirada del área temporal puede causar FileNotFoundException desde el monitor
  y ocultar una finalización exitosa. Es distinta del clean de instalaciones de esta
  tarjeta; la referencia facilita encontrar la evaluación, sin incorporar su
  corrección al alcance ni añadir una dependencia.

## Límites

Sin deduplicación, barrido de YAML desconocidos o cascadas de archivos ajenos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
