---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 070-m3-dashboard-integrado
- 073-m3-integracion-cli
created_at: 2026-10-21T23:26:07.450587100Z
updated_at: 2026-10-21T23:26:07.450587100Z
---

# Acreditar y preparar el paquete integrado Native AOT de M3

## Objetivo

Aceptar M3 como recorrido completo: levantar proyecto y vigilar en terminal prompts, tokens, presets, modelos, recursos y logs, además de completar sus operaciones CLI.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [README.md](../../docs/README.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Ejecutar receta global importada, consumidor de default/servicio nombrado y proyecto self desde archivos manuales/recibidos, sin requerir init o editor.
- [ ] Acreditar name raíz único, entidades independientes, rename de carpeta con estado local, down bajo nombres previos y nuevo up sin afectar consumidores ajenos.
- [ ] Probar preparación, inicio/recuperación, INI y refresh en up/down, import idempotente/selectivo/reemplazo, update/upgrade, verify y limpieza con propiedad/pins/referencias.
- [ ] Ejecutar los correctivos 041/042/043 y las conveniencias 071/072; comprobar formatos legibles y sintaxis de ayuda sin capacidades planificadas presentadas como implementadas.
- [ ] Acreditar dashboard 070, ps finito/continuo y logs entre reinicios con falta de telemetría, recursos compartidos y cierre sin efectos.
- [ ] Probar concurrencia, fallos de red/integridad/candidata, incertidumbre remota, copia/traslado, pérdida de home y recursos locales existentes sin redescarga injustificada.
- [ ] Ejecutar regresión M1/M2 que siga vigente, publicar Native AOT win-x64 y probar el paquete extraído con recursos acordados; preparar notas y evidencia para revisión explícita.
- [ ] Repetir el escenario visual completo de 070 desde paquete extraído: proyecto manual, varios presets/entradas, peticiones reales, fases de prompt/generación, recursos y logs en una pantalla. Entregar evidencia visual y limitaciones comprobadas; no aceptar sólo CLI o capturas de fixtures.

## Dependencias

- [070: Integrar y acreditar el dashboard Servicios completo](../070-m3-dashboard-integrado/README.md)
- [073: Completar cobertura y ayuda de la CLI del milestone](../073-m3-integracion-cli/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Hallazgos relacionados

- [002: Coordinación entre monitor de descargas y limpieza](../../findings/002-coordinacion-monitor-limpieza.md).
  Consultar su evidencia al interpretar regresiones de descargas, publicación y
  recuperación Native AOT: el monitor puede ocultar una finalización exitosa con
  FileNotFoundException y una repetición correcta no descarta la carrera. El registro
  distingue reproducción controlada, atribución y límites; esta referencia no añade
  una dependencia ni autoriza implementar la corrección dentro de la aceptación.

## Límites

Sin release/tag/push, instalación global, commits automáticos, descargas costosas implícitas ni incorporar M4 como requisito oculto.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
