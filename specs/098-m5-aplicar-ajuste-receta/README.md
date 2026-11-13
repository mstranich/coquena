---
status: draft
created: 2026-11-12
tags:
- m5
- tool
created_at: 2026-11-12T03:19:48.433057Z
updated_at: 2026-11-13T17:02:30.941407700Z
---
# Guardar una recomendación MoE en la receta

## Objetivo

Convertir una recomendación obtenida por Coquena en cambios explícitos del preset,
respetando las combinaciones YAML. Entrega de [M5](../../docs/milestones/M5-rendimiento.md).
Sustituye el borrador de comparación general de mediciones.

## Alcance

- Operación separada de medir; nombre y argumentos exactos por concretar.
- Seleccionar una recomendación válida conservada por 099 y el preset destino.
- Mostrar valores actuales, propuestos, backend de calibración, combinación y YAML afectado.
- Guardar sólo diferencias confirmadas: CLI en última capa seleccionada; TUI puede
  crear un override. No copiar un snapshot completo ni publicar/reiniciar.
- Comprobar conflictos y validar la composición resultante con las operaciones
  existentes de edición; guardar no convierte una predicción en mejora medida.
- Sin receta editable, conservar la recomendación consultable sin crear YAML implícitamente.

Reglas: [recomendaciones](../../docs/design/benchmarking.md#recomendaciones-y-receta)
y [edición por capas](../../docs/design/definiciones-declarativas.md#combinación-y-rutas).

## Dependencia

099 proporciona una propuesta identificada con evidencia y configuración de origen.
No se exige administrador de bancos, tabla histórica de variantes ni ranking general.

## Por concretar

- Sintaxis del comando de aplicación y referencia a la recomendación.
- Comportamiento cuando cambió la configuración desde la calibración o se midió
  con otro backend; no cambiar el backend declarado implícitamente.

## Aceptación

- [ ] Propuesta aplicable mostrada y guardada sólo con confirmación.
- [ ] Pruebas con combinación de capas, override y cambios externos.
- [ ] Fallo o cancelación no modifica parcialmente la receta.
- [ ] No reinicia ni publica el preset al guardar.
- [ ] Publicación y ejecución Windows Native AOT acreditadas.
