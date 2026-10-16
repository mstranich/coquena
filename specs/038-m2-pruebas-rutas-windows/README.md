---
status: planned
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:31.646847900Z
updated_at: 2026-10-16T20:07:59.209311200Z
---

# Comparar rutas Windows correctamente en las pruebas de runtime

## Problema y evidencia

La suite nativa de la prueba Ornith falló en "external start uses registered
executable and ID" al utilizar D:\Coquena en las rutas de validación. Al usar
la capitalización real D:\coquena superó esa comprobación. Se observan comparaciones
textuales sensibles a mayúsculas en ServerRuntimeTests para ExecutablePath, tanto
en el backend externo como en el trasladado.

La verificación del proceso en WindowsServerProcess ya compara rutas ignorando
capitalización. La evidencia apunta a un falso negativo del test; no demuestra
una identidad equivocada del proceso real. Confirmar los operandos al reproducir.

## Objetivo y alcance

Corregir las comparaciones de rutas de las pruebas para el destino Windows x64,
manteniendo por separado las comprobaciones estrictas de GUID e identidad del
proceso. No debilitar protecciones de producción ni extender soporte de plataformas.
Limitar la revisión a aserciones equivalentes afectadas, sin refactorización general.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Servidores](../../docs/servidores.md).
- [Evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: tests/Coquena.Tests/ServerRuntimeTests.cs; comparación de referencia
  en src/Coquena/Servers/WindowsServerProcess.cs.
- Antecedente completo: 035. La corrección de codificación es independiente.

## Trabajo y aceptación

- [ ] Reproducir y registrar ambos operandos con rutas equivalentes de distinta
  capitalización, usando un home temporal aislado.
- [ ] Corregir las aserciones para que la misma ruta Windows no falle por capitalización,
  conservando el rechazo de rutas distintas y BackendId incorrectos.
- [ ] Cubrir backend externo y backend trasladado; no exigir renombrar carpetas reales.
- [ ] Ejecutar pruebas pertinentes con capitalizaciones distintas y con la ruta real.
- [ ] Publicar y ejecutar producto/runner Native AOT win-x64; evidencia y temporales en D:.
- [ ] Registrar resultados y límites. Presentar para revisión, sin corregir producción,
  cerrar, commitear o publicar por la mera creación de esta tarjeta.
