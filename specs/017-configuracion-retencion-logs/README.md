---
status: planned
created: 2026-10-09
priority: medium
created_at: 2026-10-09T14:07:09.965469Z
updated_at: 2026-10-10T12:15:33.140541Z
transitions:
- status: draft
  at: 2026-10-09T14:07:24.090154500Z
- status: planned
  at: 2026-10-10T12:15:33.140541Z
---
# Configuración de retención de logs y separación de config/preset

## Objetivo
Liberar config para las políticas de Coquena, trasladar la asignación de parámetros a
preset e incorporar la retención global y por servidor.

## Referencias
- [M1: servidor local](../../docs/milestones/M1-servidor-local.md)
- [CLI](../../docs/comandos.md#config) y [presets](../../docs/comandos.md#preset)
- [Valores de presets](../../docs/modelos-y-companions.md#presets)
- [Retención y unidades](../../docs/servidores.md#consulta-de-logs)
- [Persistencia](../../docs/servidores.md#persistencia)
- Antecedentes implementados: [010](../010-m1-config-presets-ini/README.md) y
  [013](../013-m1-metricas-logs/README.md).

## Trabajo
- [ ] Sustituir config set/unset/show --preset por preset set/unset/show.
- [ ] Retirar los ámbitos global/family/modelo, sus valores compartidos y su composición
  en el INI. Cada preset conserva sus propios valores; server set administra los del proceso.
- [ ] Eliminar la sintaxis anterior sin alias, migraciones ni avisos de deprecación.
- [ ] Implementar config log-retention show/set/unset y
  server [nombre] log-retention show/set/unset, con persistencia segura.
- [ ] Aplicar la política efectiva en la limpieza existente y actualizar la ayuda.

## Aceptación: asignación y CLI
- [ ] Set, unset y show operan sobre el preset indicado y su modelo asociado.
- [ ] Dos presets del mismo modelo mantienen valores independientes; unset no recupera
  valores globales, de familia ni del modelo.
- [ ] Dos servidores con el mismo backend mantienen parámetros de proceso independientes.
- [ ] Verificar la composición del INI conforme al
  [alcance por instancia](../../docs/catalogo-de-parametros.md#aplicación-en-router-mode),
  sin incorporar los ámbitos retirados.
- [ ] La sintaxis retirada recibe el diagnóstico normal de argumentos inválidos.

## Aceptación: retención
- [ ] Sin política propia, cada servidor usa el límite inicial de 100 MiB.
- [ ] Set/unset global modifica/restablece la política; el override de servidor prevalece
  y su unset recupera el global. Cambiar el global no altera los overrides existentes.
- [ ] La política persiste entre invocaciones; sin nombre se usa el servidor predeterminado.
- [ ] Show informa bytes exactos, tamaño legible y origen inicial/global/propio.
- [ ] Consultar o cambiar la política no elimina archivos. La limpieza al iniciar,
  detener o consultar logs usa el límite efectivo y conserva activo y último con diagnóstico.
- [ ] Un fallo de validación o persistencia no deja una actualización parcial.

## Aceptación: tamaños
- [ ] KB/MB/GB usan base decimal; KiB/MiB/GiB usan base binaria; B y ausencia de sufijo
  expresan bytes. 100MB = 100000000 B; 100MiB = 104857600 B.
- [ ] 1.5GiB = 1610612736 B; 0.5KiB = 512 B; 0.1KiB se rechaza sin redondear.
- [ ] 100MiB, 100mib y "100 MiB" son equivalentes, independientemente de la cultura del SO.
- [ ] Rechazar cero, negativos, coma decimal, separadores de miles, unidades desconocidas,
  fracciones de byte y overflow. Verificar los extremos del rango documentado.

## Validación y límites
- [ ] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 con evidencia.
- [ ] PlantUML, JSON, enlaces y git diff --check para la documentación afectada.

La gestión del catálogo de parámetros pertenece a 018; los directorios y system probe,
a sus tarjetas de M2. Companions, clonación, filtrado de parámetros personalizados y
cambios de métricas no forman parte de esta entrega. Se reutilizan la limpieza y el
ciclo de procesos existentes; no se agrega rotación continua.
