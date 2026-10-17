---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 057-m3-verificacion
- 058-m3-novedades
created_at: 2026-10-21T23:26:05.844068300Z
updated_at: 2026-10-21T23:26:05.844068300Z
---

# Aplicar planes selectivos con publicación conjunta recuperable

## Objetivo

Aplicar exactamente un plan vigente conservando activo y resoluciones anteriores ante fallo de cualquier unidad seleccionada.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [actualizacion-y-limpieza.md](../../docs/actualizacion-y-limpieza.md)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [descargas.md](../../docs/descargas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Validar vigencia y selector de plan; plan vencido exige nueva consulta, sin resolver otra versión dentro del upgrade.
- [ ] Preparar y verificar todas las unidades seleccionadas antes del commit de resoluciones/inventario, sin éxito parcial de ese conjunto.
- [ ] Respetar pin fijo hasta cambio explícito a latest y plan nuevo; preservar variante y protección del build fijado anterior.
- [ ] Permitir transferencia con servicio activo conservando revisiones físicas, última buena y referencias de otros consumidores.
- [ ] Probar fallos, reanudación, concurrencia y cierre entre fases; informar disponibilidad para siguiente ejecución/aplicación sin reiniciar ni editar intención.

## Dependencias

- [057: Verificar integridad y mantener diagnósticos locales por recurso](../057-m3-verificacion/README.md)
- [058: Consultar novedades y persistir planes exactos por selección](../058-m3-novedades/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin limpieza automática por número de builds, sustitución del activo o recuperación de proceso ajena al contrato de 054.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
