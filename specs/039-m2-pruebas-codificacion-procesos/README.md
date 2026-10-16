---
status: planned
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:32.586730500Z
updated_at: 2026-10-16T20:07:59.286208200Z
---

# Eliminar dependencia de la página de códigos en las pruebas de procesos

## Problema y evidencia

La prueba "recent logs bounded and UTF8 intact" falló con la consola original y
la suite completa pasó tras ejecutar chcp 65001. El caso escribe "recent UTF8 á fin"
y comprueba su lectura vía CLI. La captura de procesos en ServerRuntimeTests y el
helper run-native-tests.ps1 no fijan explícitamente la codificación de las salidas.

Está comprobada la sensibilidad del procedimiento al entorno; aún hay que aislar
si la divergencia está en la emisión de la CLI, la decodificación de procesos, la
captura del runner o varias de ellas. No atribuir corrupción al archivo de log sin
comprobar sus bytes. La comparación de rutas de 038 es otro defecto independiente.

## Objetivo y alcance

Hacer reproducible la comprobación de logs y diagnósticos con caracteres no ASCII
sin exigir modificar manualmente la página de códigos global. Definir y comprobar
el contrato entre productor y lector en los puntos afectados. Comenzar por el
harness de pruebas; cualquier cambio observable a la codificación de la CLI debe
justificarse contra la documentación y consultarse si queda una decisión funcional.

Conservar la comprobación de integridad textual, límites del log y códigos de salida.
No resolver el fallo eliminando acentos ni relajando la aserción a texto ASCII.
No rediseñar logging, traducciones ni comandos.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Servidores y logs](../../docs/servidores.md), [CLI](../../docs/comandos.md).
- [Evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: tests/Coquena.Tests/ServerRuntimeTests.cs y
  specs/035-m2-entrega-integrada/run-native-tests.ps1; inspeccionar los productores
  concretos de salida antes de decidir una corrección adicional.
- Antecedentes completos: 013 y 035. Sin bloqueo de 038 para un caso enfocado.

## Trabajo y aceptación

- [ ] Reproducir en consola UTF-8 y al menos una página de códigos Windows distinta,
  registrando bytes del log, bytes emitidos y texto capturado para localizar la divergencia.
- [ ] Garantizar que la captura y su productor usan un contrato compatible sin depender
  de chcp global; conservar stdout y stderr y diagnósticos no ASCII.
- [ ] Probar una cadena con acentos y caracteres fuera de la página de códigos elegida,
  así como el límite de lectura reciente del log.
- [ ] Ejecutar la regresión enfocada en ambos entornos y la suite pertinente; no
  presentar normalización externa como una corrección del código.
- [ ] Publicar y ejecutar producto/runner Native AOT win-x64, con temporales en D:.
- [ ] Registrar decisiones, posibles cambios funcionales y evidencia. Requiere revisión;
  crear esta tarjeta no autoriza implementación, cierre, commit ni publicación.
