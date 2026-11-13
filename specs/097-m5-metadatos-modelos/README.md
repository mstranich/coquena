---
status: draft
created: 2026-11-12
tags:
- m5
- tool
created_at: 2026-11-12T03:19:48.198743300Z
updated_at: 2026-11-13T17:02:30.933982400Z
---
# Indexar metadatos MoE al descargar y verificar

## Objetivo

Guardar evidencia de arquitectura y expertos sin releer GGUF al listar presets.
Parte de [M5](../../docs/milestones/M5-rendimiento.md). Esta tarjeta sustituye el
borrador de carga de servidor y separa el soporte de metadatos de la calibración.

## Alcance

- Reutilizar validación final de descarga para persistir arquitectura, contadores
  de expertos, tipos, clasificación y versión de interpretación con su instalación.
- Ampliar datos incompletos mediante verify y cabeceras locales, sin --checksum,
  redescarga, modificación de pesos o cambio de referencias de integridad.
- Obtener evidencia al registrar externos y completar modelos existentes una vez.
- No transformar ausencia de información en modelo denso; guardar diagnósticos.
- Invalidar evidencia cuando cambien revisión/archivos y conservar asociación
  correcta durante publicación, recuperación y escrituras concurrentes.
- Consumir el índice en selección de presets: priorizar confirmados, permitiendo
  también intentos con desconocidos o densos. No imponer una lista cerrada de arquitecturas.

Reglas: [modelos](../../docs/design/modelos-y-companions.md#evidencia-de-arquitectura-y-expertos),
[descargas](../../docs/design/descargas.md) y
[verify](../../docs/design/actualizacion-y-limpieza.md#verificación-y-bloqueos).

## Dependencias y límites

No bloquea la prueba de viabilidad 096: ésta usa modelo y backend explícitos.
Descarga, índice y verify permanecen juntos en esta entrega de metadatos.
No incorpora pruebas de carga ni pretende certificar compatibilidad del ajuste.

## Aceptación

- [ ] Descarga y registro externo guardan evidencia con la instalación correcta.
- [ ] verify completa índices anteriores dentro de su selección de recursos.
- [ ] Consultas repetidas reutilizan el índice sin leer otra vez el GGUF.
- [ ] Actualización, contención y recuperación no mezclan evidencia de instalaciones.
- [ ] Fallos de lectura conservan diagnóstico y no fabrican clasificación.
- [ ] Pruebas pertinentes y ejecución Windows Native AOT acreditadas.
