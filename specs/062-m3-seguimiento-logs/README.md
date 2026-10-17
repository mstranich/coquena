---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 061-m3-logs-hooks
created_at: 2026-10-21T23:26:06.166381400Z
updated_at: 2026-10-21T23:26:06.166381400Z
---

# Seguir logs por identidad de servicio entre reinicios y rotación

## Objetivo

Mantener un observador continuo de logs y permitir consulta histórica sin controlar el servicio.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md#logs](../../docs/observabilidad.md#logs)
- [comandos.md](../../docs/comandos.md)
- [ui-servicio.md](../../docs/ui-servicio.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar service/server logs list, show por identificador, follow/-f y atajo logs con selección equivalente de servicio/default.
- [ ] Seguir identidad de servicio, drenar archivo anterior y continuar en el nuevo tras reinicio/rotación con separación visible; no enlazar otra entidad que reutilice nombre.
- [ ] Esperar mientras el servicio esté detenido y cancelar sólo el observador; tratar truncado, desaparición y permisos sin bucle intensivo.
- [ ] Listar servicio/instancia/fecha/tamaño legible/activo y mantener consulta histórica fija sin convertirla en follow.
- [ ] Probar dos reinicios, rotación, escritura concurrente, servicio sin log inicial y retención protegida; exponer lector reutilizable por TUI.

## Dependencias

- [061: Configurar logs y notificar fallos de inicio tras recuperación](../061-m3-logs-hooks/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin colector residente, cambio de inferencia ni absorber el hook de 061.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
