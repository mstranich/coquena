---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
created_at: 2026-10-21T23:26:07.256314800Z
updated_at: 2026-10-21T23:26:07.256314800Z
---

# Inicializar una receta mínima sin fabricar estado de ejecución

## Objetivo

Ofrecer init como conveniencia independiente, preservando el uso de recetas creadas externamente.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [formato-proyecto.md#inicialización](../../docs/formato-proyecto.md#inicialización)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Crear coquena.yaml mínimo, .coquena/ y exclusiones Git según formato, sin convertir un documento mínimo en servicio listo para iniciar.
- [ ] Preservar archivos y reglas existentes e informar conflictos; repetir no sobrescribe intención ni reinicia estado.
- [ ] No fabricar inventario, índices de combinaciones, resoluciones, registro de nombres, activo o última buena sin operación que los justifique.
- [ ] Probar carpeta nueva/existente, YAML manual y repetición, sin red, procesos ni git init implícito.

## Dependencias

Sin bloqueos de implementación dentro de M3. Referenciar documentación no crea una dependencia.

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

No depende del compositor y no es requisito para recibir, leer o usar una receta. Sin editor, preparación de recursos o migración M2.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
