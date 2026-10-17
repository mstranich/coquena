---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 051-m3-resoluciones-preparacion
created_at: 2026-10-21T23:26:05.686014800Z
updated_at: 2026-10-21T23:26:05.686014800Z
---

# Verificar integridad y mantener diagnósticos locales por recurso

## Objetivo

Auditar la selección y exponer fallos conocidos sin convertir cada inicio en lectura completa de pesos.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [modelos-y-companions.md](../../docs/modelos-y-companions.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Verificar definición y existencia/integridad completa de backend, principales, shards y companions seleccionados, incluyendo inventario no usado sólo mediante opción explícita.
- [ ] Persistir resultado local por recurso separado de referencias; no cambiar YAML, resoluciones o hashes esperados ni descargar/reparar.
- [ ] Distinguir evidencia insuficiente, fallo, indisponibilidad e integridad acreditada; baseline externa explícita es otra acción, no reparación de una discrepancia.
- [ ] Resolver un fallo concreto sólo por comprobación satisfactoria o descarga completa verificada; excluir recurso no afirma haberlo reparado.
- [ ] Consultar fallos conocidos a bajo costo al iniciar y probar preservación del activo, referencias cruzadas y falta de hash comparable.

## Dependencias

- [051: Persistir combinaciones y preparar recursos con resoluciones reproducibles](../051-m3-resoluciones-preparacion/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin --fix, auditoría implícita por listar/iniciar ni marcar incompatibilidad de hardware como fallo de integridad. No bloquea el primer ciclo de servicios válidos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
