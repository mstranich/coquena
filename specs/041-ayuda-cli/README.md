---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
- correctivo
created_at: 2026-10-21T23:16:24.363330300Z
updated_at: 2026-10-21T23:16:24.363330300Z
---

# Organizar la ayuda según la sintaxis real del ejecutable

## Objetivo

Hacer legible la ayuda actual y permitir identificar exactamente los argumentos y opciones de cada operación.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)

Las reglas están en la documentación funcional enlazada. Esta tarjeta delimita trabajo y aceptación; no redefine esas reglas.

## Trabajo y aceptación

- [ ] Contrastar la ayuda con los parsers actuales; separar familias, sintaxis y explicaciones breves sin anticipar comandos todavía no implementados.
- [ ] Mostrar argumentos obligatorios, opcionales, repetibles y opciones locales; eliminar cadenas extensas que mezclan operaciones diferentes.
- [ ] Precisar dónde se admite --dry-run y rechazar la interpretación de --dry o de una opción de simulación global.
- [ ] Explicar los selectores de recurso/operación y el alcance real de -d y -f; comprobar ayuda, aliases existentes y errores sin efectos sobre estado.
- [ ] Conservar la semántica de las operaciones: esta entrega sólo modifica su explicación y presentación.

## Dependencias

Sin bloqueos de implementación dentro de M3. Las referencias funcionales no son dependencias.

## Validación prevista

Pruebas focalizadas de los escenarios anteriores, incluidos errores y preservación del estado. Para cambios de runtime: publicación Native AOT win-x64 y ejecución del binario publicado con estado de prueba en D:. Evaluar compatibilidad AOT antes de incorporar dependencias. Registrar resultados reproducibles al implementar; no asumir descargas costosas ni usar datos reales sin autorización.

## Límites

Sin nuevos comandos u opciones, implementación de recetas, cambio de unidades ni instalación global de la herramienta. La ayuda de las operaciones nuevas se entrega en sus tarjetas; la cobertura integrada se revisa en [073](../073-m3-integracion-cli/README.md).

## Estado y evidencia

Planificada. Sin implementación ni validación ejecutadas. El inicio del trabajo, el cierre y cualquier commit requieren la autorización correspondiente.
