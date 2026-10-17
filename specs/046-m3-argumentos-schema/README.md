---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 045-m3-lectura-recetas
created_at: 2026-10-21T23:26:04.830589Z
updated_at: 2026-10-21T23:26:04.830589Z
---

# Validar argumentos y emitir CLI e INI con schema optativo

## Objetivo

Entregar validación y emisión segura de argumentos según el schema opcional del ámbito seleccionado.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [servidores.md](../../docs/servidores.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar arg-defs imports/definitions, metaschema y reglas cerradas de tipos, ámbitos, aliases, restricciones y relaciones declarativas sin ejecutar código aportado.
- [ ] Respetar args-mode permisivo por defecto y estricto global/local; validar argumentos conocidos en ambos modos y diagnosticar desconocidos en estricto.
- [ ] Emitir booleanos, literales, listas, repeticiones, separadores, null y referencias resueltas sin inferir aridad o escapes incompatibles con el destino.
- [ ] Pasar tokens CLI sin shell y usar codec INI acreditado por adaptador/build; rechazar valores sin representación reversible y composiciones imposibles por preset.
- [ ] Entregar operaciones de dominio de param y consulta de impacto reutilizables; la persistencia puntual de comandos en YAML se integra en 066.
- [ ] Probar schema importado/inline y precedencia, aliases conflictivos, listas, tipado de interpolación, reglas inválidas y ausencia de defaults materializados automáticamente.

## Dependencias

- [045: Leer y componer recetas YAML y variables desde archivos manuales](../045-m3-lectura-recetas/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No depende de editar YAML desde CLI. Sin formularios, generación IA de schemas ni atribuir APIs de control al catálogo de argumentos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
