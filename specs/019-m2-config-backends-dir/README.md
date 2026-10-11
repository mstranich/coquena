---
status: planned
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.328913500Z
updated_at: 2026-10-10T16:29:42.160543200Z
transitions:
- status: planned
  at: 2026-10-10T16:29:42.160543200Z
---
# Configurar el directorio de backends

## Objetivo
Consultar y ajustar la ubicación de los backends gestionados mediante config backends-dir.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Directorios gestionados](../../docs/servidores.md#directorios-gestionados)
- [Persistencia](../../docs/servidores.md#persistencia)

## Trabajo y aceptación
- [ ] Persistir el directorio y usarlo en instalación, consulta y resolución de backends.
- [ ] Verificar publicación y traslado entre volúmenes con copia y comprobación antes
  de retirar el origen, usando la preparación existente y recuperación ante interrupciones.
- [ ] Verificar cambio/restablecimiento, reinicio de Coquena y fallos de acceso o escritura.
- [ ] Crear destinos inexistentes y rechazar rutas a archivos o sin permisos suficientes
  antes de modificar la ubicación o trasladar recursos.
- [ ] Verificar show/set/unset y el origen de la ruta efectiva; unset recupera
  <home>/backends y ofrece el mismo traslado que set.
- [ ] Conservar rutas relativas en la configuración y verificar su resolución después
  de reubicar el conjunto de directorios con home como base, incluso al invocar desde
  otra carpeta. Verificar las ubicaciones de instalación antiguas y nuevas del catálogo.
- [ ] Comprobar el tratamiento acordado de instalaciones, descargas y servidores existentes.
- [ ] Probar elección de traslado, conservación en origen y cancelación; mantener
  identidades y referencias y resolver instalaciones en ambas ubicaciones.
- [ ] Bloquear el traslado completo ante recursos en uso o descargas pendientes;
  comprobar el diagnóstico y la ausencia de cambios en archivos y configuración.
- [ ] Publicar y ejecutar Native AOT win-x64; registrar evidencia y actualizar ayuda.
- [ ] Verificar fusión con contenido existente y reporte completo de colisiones antes
  de mover archivos; comprobar confirmación única, sobrescrituras autorizadas y cancelación.
- [ ] Conservar archivos sin colisión y proteger recursos en uso o externos en el destino;
  no sobrescribir colisiones nuevas que no estén incluidas en la confirmación.

## Límites
Esta entrega configura la ubicación de backends; no implementa actualización de binarios
ni el ajuste del directorio de modelos.
