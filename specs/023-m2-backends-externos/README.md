---
status: planned
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:35:37.958318600Z
updated_at: 2026-10-10T16:48:26.225918200Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.225918200Z
---
# Registrar y seleccionar backends externos

## Objetivo
Implementar backend register y la selección de instalaciones externas por nombre.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#backend)
- [Identificación](../../docs/servidores.md#identificación-de-backends)

## Trabajo y aceptación
- [ ] Validar ruta, nombre, familia declarada y sondeo local válido de --version; persistir
  ID, propiedad externa y distribución declarada, con origen oficial implícito.
- [ ] Probar colisiones de nombres y selectores; distinguir un externo de un gestionado
  con igual origen, familia y release.
- [ ] Seleccionar el externo mediante backend use y server use; iniciar/reiniciar el
  servidor con ese binario sin incorporarlo a latest o a descargas oficiales.
- [ ] Verificar registro/listado sin copiar archivos; fallos de sondeo o persistencia
  no crean registros parciales ni alteran la selección previa.
- [ ] Actualizar ayuda y documentación afectadas; ejecutar pruebas pertinentes y
  publicar/ejecutar Native AOT win-x64 con evidencia reproducible.

## Límites
No incorpora fuentes de descarga de terceros ni gestión de nombres cortos de orígenes.
No certifica aceleración ni procedencia a partir del nombre del ejecutable.
