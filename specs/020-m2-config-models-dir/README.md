---
status: planned
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.413246100Z
updated_at: 2026-10-10T16:29:42.244111800Z
transitions:
- status: planned
  at: 2026-10-10T16:29:42.244111800Z
---
# Configurar el directorio de modelos

## Objetivo
Consultar y ajustar la ubicación de modelos y companions gestionados mediante config models-dir.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Directorios gestionados](../../docs/servidores.md#directorios-gestionados)
- [Propiedad y registros](../../docs/modelos-y-companions.md)

## Trabajo y aceptación
- [ ] Persistir el directorio y usarlo al descargar, listar y resolver archivos para presets.
- [ ] Verificar publicación y traslado entre volúmenes con copia y comprobación antes
  de retirar el origen, usando la preparación existente y recuperación ante interrupciones.
- [ ] Crear destinos inexistentes y rechazar rutas a archivos o sin permisos suficientes
  antes de modificar la ubicación o trasladar recursos.
- [ ] Verificar show/set/unset y el origen de la ruta efectiva; unset recupera
  <home>/models y ofrece el mismo traslado que set.
- [ ] Conservar rutas relativas en la configuración y verificar su resolución después
  de reubicar el conjunto de directorios con home como base, incluso al invocar desde
  otra carpeta. Verificar las ubicaciones de instalación antiguas y nuevas del catálogo.
- [ ] Verificar modelos de un archivo y unidades con shards, conservando su identidad.
- [ ] Comprobar el tratamiento acordado de recursos existentes y fallos sin estado parcial.
- [ ] Probar elección de traslado, conservación en origen y cancelación; resolver
  unidades en ambas ubicaciones, manteniendo identidades y referencias de presets.
- [ ] Bloquear el traslado completo ante recursos en uso o descargas pendientes;
  comprobar el diagnóstico y la ausencia de cambios en archivos y configuración.
- [ ] Publicar y ejecutar Native AOT win-x64; registrar evidencia y actualizar ayuda.
- [ ] Verificar fusión con contenido existente y reporte completo de colisiones antes
  de mover archivos; comprobar confirmación única, sobrescrituras autorizadas y cancelación.
- [ ] Conservar archivos sin colisión y proteger recursos en uso o externos en el destino;
  no sobrescribir colisiones nuevas que no estén incluidas en la confirmación.

## Límites
Esta entrega no ajusta el directorio de backends. Configurar la ubicación de companions
gestionados no incorpora su instalación: ese recorrido pertenece a la entrega de companions.
