---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 046-m3-argumentos-schema
- 048-m3-backends
- 050-m3-archivos-locales
created_at: 2026-10-21T23:26:05.250489100Z
updated_at: 2026-10-21T23:26:05.250489100Z
---

# Persistir combinaciones y preparar recursos con resoluciones reproducibles

## Objetivo

Preparar una combinación de receta sin resolver novedades ajenas ni acreditar ejecución por completar archivos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Calcular identidad canónica de combinación y mantener índices compartibles/locales sin descubrir archivos por barrido.
- [ ] Persistir resoluciones portables separadas de inventario, intención expandida local y snapshot de última buena; no filtrar secretos ni hashes de secretos a documentos compartibles.
- [ ] Reutilizar resoluciones de intención sin cambios y resolver sólo cambios explícitos/faltantes; no exigir update para preparar una edición válida.
- [ ] Separar recursos propios del servicio y del consumidor; preparar sólo referencias habilitadas y proteger las cruzadas entre ámbitos.
- [ ] Publicar el conjunto preparado con generaciones y control de revisión, sin mezclar revisiones ni sobrescribir activos; una preparación no promueve última buena.
- [ ] Probar concurrencia, cierre antes/después de commit, recursos existentes, pérdida de home y traslado con estado local conservado.

## Dependencias

- [046: Validar argumentos y emitir CLI e INI con schema optativo](../046-m3-argumentos-schema/README.md)
- [048: Resolver backends de servicios globales y propios](../048-m3-backends/README.md)
- [050: Reutilizar pesos locales y resolver referencias tipadas sin redescarga](../050-m3-archivos-locales/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin arrancar servicios, aplicar presets ni implementar recuperación de proceso. No exige init o editor CLI.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
