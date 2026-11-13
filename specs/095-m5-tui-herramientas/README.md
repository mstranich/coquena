---
status: draft
created: 2026-11-12
tags:
- m5
- tui
- tool
created_at: 2026-11-12T03:19:47.735278600Z
updated_at: 2026-11-13T17:02:30.915796200Z
---
# Herramientas: Chat y Ajuste fino MoE

## Objetivo

Integrar dos recorridos concretos en la TUI: comprobar rápidamente un preset y
obtener un ajuste de caché MoE guardable en una receta. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance

- Pestaña Herramientas con categorías Chat y Ajuste fino MoE.
- Descripción de Ajuste fino MoE: «Calibrar la caché de expertos y recomendar
  parámetros para el preset».
- Formularios que usan las operaciones CLI, sin gestión general de benchmarks.
- Mostrar calibración, datos medidos, predicciones, recomendación y errores.
- Guardado confirmado mediante la operación de 098, con diferencia y YAML destino.
- Chat y acceso rápido desde En vivo se implementan en 100; esta tarjeta integra
  su navegación sin duplicar el cliente o su telemetría.

## Dependencias

- 099 para ejecutar calibraciones; 098 para guardar propuestas. No depende de Bench.
- 100 aporta Chat; su diseño puede avanzar sin esperar al port MoE.

## Por concretar

- Vista previa y disposición a 80×24, progreso real y cancelación.
- Qué detalle de evidencia se muestra sin saturar la pantalla.

## Aceptación

- [ ] Vista previa revisada antes de implementar.
- [ ] Recorrido completo: seleccionar, calibrar, revisar y guardar sin publicación implícita.
- [ ] Error o cancelación conserva una interfaz utilizable.
- [ ] Publicación Windows Native AOT y ejecución real, además de revisión visual.
