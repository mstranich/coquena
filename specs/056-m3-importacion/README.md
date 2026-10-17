---
status: planned
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 055-m3-publicacion-presets
created_at: 2026-10-21T23:26:05.613396Z
updated_at: 2026-10-21T23:26:05.613396Z
---

# Importar recetas completas o selectivas con reemplazo explícito

## Objetivo

Copiar intención seleccionada al ámbito global y preparar/publicar lo correspondiente sin seguir o modificar la receta original.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servicios-recetas.md#importación-global](../../docs/servicios-recetas.md#importación-global)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [comandos.md](../../docs/comandos.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Implementar import, categorías --select, --as-default y dependencias excluidas conforme al contrato; no incorporar categorías ocultas.
- [ ] Mantener un name raíz y registros independientes de servicio/presets/modelos/parámetros; nombre raíz identifica servicio y prefija publicaciones, sin prefijar archivos de recursos o definiciones de parámetros.
- [ ] Probar idempotencia de intención idéntica y rechazo por defecto de conflictos; implementar --replace por entidad y --force para el archivo completo con sus incompatibilidades.
- [ ] Conservar revisiones activas, pins, propiedad y referencias de otros consumidores incluso con reemplazo autorizado; --force no omite validaciones ni renombra implícitamente.
- [ ] Materializar intención/schema importados y conservar base de externos sin copiar binarios/pesos; no congelar secretos expandidos ni copiar env a intención compartible.
- [ ] No iniciar servicio al importar; publicar sólo presets seleccionados si está activo, o conservar para inicio si está detenido. Registrar incertidumbre/remanentes sin presentar aplicación completa falsa.
- [ ] Probar import completo/selectivo, colisiones, alias default, fallo de publicación, concurrencia y ausencia de cambios en original/externos.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin importación remota de repositorios, migración M2, proveedores nuevos ni adopción de externos.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
