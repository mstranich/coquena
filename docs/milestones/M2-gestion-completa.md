# M2 — Completar la gestión de catálogos

**Estado:** aceptado el 13 de octubre de 2026. **Depende de:** [M1](M1-servidor-local.md).

## Objetivo

Segundo entregable: ampliar el recorrido ejecutable de M1 con las funciones del alcance
inicial que se recortaron para conseguir primero un servidor útil. Mantener C#/.NET 10,
Native AOT y las reglas canónicas. La evidencia de aceptación está en
[la entrega integrada](../../specs/035-m2-entrega-integrada/README.md).

## Referencias

- [CLI](../comandos.md)
- [Modelos y companions](../modelos-y-companions.md)
- [Parámetros](../catalogo-de-parametros.md)
- [Servidores](../servidores.md)
- [Descargas](../descargas.md)

## Entregas

La gestión de backends oficiales conserva el alcance de familias de
[M1](M1-servidor-local.md#alcance-propuesto): CPU, CUDA y Vulkan. ROCm, SYCL,
OpenVINO y otras familias no se incorporan automáticamente a M2; requieren una
ampliación de alcance aprobada explícitamente. Esto no redefine la familia declarada
al registrar un backend externo ni certifica su compatibilidad.

- [x] Registrar y seleccionar backends externos.
      ([023](../../specs/023-m2-backends-externos/README.md)).
- [x] Consultar novedades de seguimientos latest oficiales.
      ([024](../../specs/024-m2-backend-update/README.md)).
- [x] Aplicar upgrades de backends y retención de builds.
      ([025](../../specs/025-m2-backend-upgrade/README.md)).
- [x] Registrar modelos y companions externos.
      ([026](../../specs/026-m2-registro-modelos-externos/README.md)).
- [x] Descargar y catalogar companions gestionados.
      ([027](../../specs/027-m2-descarga-companions/README.md)).
- [x] Actualizar observaciones locales y verificar integridad de modelos/companions.
      ([028](../../specs/028-m2-refresh-integridad-local/README.md)).
- [x] Intentar emparejamiento remoto opcional de externos.
      ([029](../../specs/029-m2-emparejamiento-externos/README.md)).
- [x] Detectar y resolver novedades de modelos/companions.
      ([030](../../specs/030-m2-model-update/README.md)).
- [x] Aplicar planes de actualización, renombres y reubicaciones.
      ([031](../../specs/031-m2-model-upgrade/README.md)).
      Implementación aprobada y validada con Native AOT win-x64.
- [x] Convertir explícitamente externos en instalaciones gestionadas.
      ([032](../../specs/032-m2-transicion-externo-gestionado/README.md)).
- [x] Gestionar definiciones y asociaciones de soporte del catálogo de parámetros
      para la distribución oficial de llama.cpp, con builds bNNNN.
      ([018](../../specs/018-m2-param-define/README.md)).
- [x] Restablecer el catálogo de fábrica con impacto y cascada confirmada.
      ([022](../../specs/022-m2-config-param-reset/README.md)).
- [x] Configurar el directorio de backends y su traslado opcional.
      ([019](../../specs/019-m2-config-backends-dir/README.md)).
- [x] Configurar el directorio de modelos/companions y su traslado opcional.
      ([020](../../specs/020-m2-config-models-dir/README.md)).
- [x] Separar el sondeo de SO/hardware mediante system probe.
      ([021](../../specs/021-m2-system-probe/README.md)).
- [x] Clonar presets y completar referencias explícitas a companions.
      ([033](../../specs/033-m2-presets-clone-companions/README.md)).
- [x] Retirar recursos con protección de propiedad, uso y referencias.
      ([034](../../specs/034-m2-retirada-recursos/README.md)).
- [x] Auditar cobertura CLI, ejecutar regresión M1 y preparar el entregable integrado.
      ([035](../../specs/035-m2-entrega-integrada/README.md)).
      Incluye la recuperación autorizada de la retirada de definiciones de servidores,
      omitida entre las entregas, y la corrección de rutas de principales externos/trasladados.

Las tarjetas concretan las entregas de esta lista y sus dependencias reales. Se reutilizan
la persistencia, las transferencias y la ejecución del recorrido M1. La separación de
config/preset de [017](../../specs/017-configuracion-retencion-logs/README.md) es antecedente
del recorrido integrado; su retención de logs conserva su entrega propia.

## Alcance futuro que requiere aclaración

Las definiciones YAML y la importación/exportación JSON de parámetros corresponden
a [M3](M3-definiciones-declarativas.md), fuera de M2. La gestión de descargas
permanece limitada a releases oficiales; registrar un ejecutable externo local
no incorpora un proveedor de terceros ni adapta su interfaz. El usuario obtiene el
runtime manualmente y registra su ejecutable local. Se descarta la gestión de
repositorios de runtimes de terceros; la asociación manual de parámetros a binarios
externos queda para una entrega posterior, fuera de 018.

El recorte de M1 también mencionaba TUI, HTTP propio, autenticación HF, fuentes de terceros
y multiplataforma. Antes de esta planificación se habían aplazado explícitamente.
No asumir que pasan todos a M2 por agruparlos en un párrafo de exclusiones: confirmar
con el usuario si su inclusión cambia ese alcance. La TUI estaba prevista después del
milestone de todas las funciones iniciales; no elegir su librería ni anticiparla.

## Aceptación propuesta

- [x] Segundo ejecutable publicado Native AOT, con funciones aceptadas y sin comandos ficticios.
- [x] Recorrido M1 sigue funcionando.
- [x] Actualizaciones conservan identidad, propiedad, revisión fijada y recuperación acordadas.
- [x] Externos no reciben actualizaciones ni eliminación física.
- [x] Pruebas funcionales y de fallos, más publicación/ejecución nativa verificadas.
