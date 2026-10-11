---
status: planned
created: 2026-10-10
tags:
- m2
depends_on:
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:44.459342600Z
updated_at: 2026-10-10T16:48:26.803296900Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.803296900Z
---
# Detectar y resolver actualizaciones de modelos y companions

## Objetivo
Detectar cambios remotos y resolver planes de actualización explicables sin modificar la
instalación.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#seguimiento-y-actualización-de-gestionados)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar model update y companion update separando comparador puro del transporte HF
  y guardando planes de commit fijo.
- [ ] Probar identidad tipada, cambio sólo de README, pesos cambiados, reubicación exacta
  única, rutas desaparecidas y evidencia no comparable.
- [ ] Resolver ambigüedad/sucesores con menú explícito sin mover archivos/descargar
  pesos/cambiar cuantización automáticamente.
- [ ] Distinguir consulta fallida/intervención/orfandad y conservar fechas de último
  intento/éxito; inventario incompleto no prueba ausencia.
- [ ] Cubrir alcance global con companions, filtro companion, externos excluidos y errores por
  unidad.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No aplica planes, mueve archivos o certifica integridad.
