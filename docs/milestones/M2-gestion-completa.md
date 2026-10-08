# M2 — Completar la gestión de catálogos

**Estado:** borrador. **Depende de:** [M1](M1-servidor-local.md).

## Objetivo

Segundo entregable: ampliar el recorrido ejecutable de M1 con las funciones del alcance
inicial que se recortaron para conseguir primero un servidor útil. Mantener C#/.NET 10,
Native AOT y las reglas canónicas. Borrador: no autoriza implementación.

## Referencias

- [CLI](../comandos.md)
- [Modelos y companions](../modelos-y-companions.md)
- [Parámetros](../catalogo-de-parametros.md)
- [Servidores](../servidores.md)
- [Descargas](../descargas.md)

## Tareas preliminares

La gestión de backends oficiales conserva el alcance de familias de
[M1](M1-servidor-local.md#alcance-propuesto): CPU, CUDA y Vulkan. ROCm, SYCL,
OpenVINO y otras familias no se incorporan automáticamente a M2; requieren una
ampliación de alcance aprobada explícitamente. Esto no redefine la familia declarada
al registrar un backend externo ni certifica su compatibilidad.

- [ ] Update/upgrade de backends: seguimientos latest, pins, instalación y retención.
- [ ] Update/upgrade de modelos y companions: revisiones fijas, correspondencias,
      candidatos interactivos, renombres físicos, integridad y protección de recursos en uso.
- [ ] Registro externo de modelos/companions, --try-match opcional y transición explícita
      a gestionado, sin actualizar ni borrar archivos externos.
- [ ] Registro y selección de backends externos por nombre, --version y distribución declarada.
- [ ] Catálogo completo de companions y referencias por parámetros; sin compatibilidad
      de inferencia propia ni descargas automáticas del principal/companion.
- [ ] Importación/exportación del catálogo de parámetros, ámbitos y diagnóstico de conflictos.
- [ ] Completar las operaciones de configuración/presets y clonación acordadas.
- [ ] Cubrir comandos restantes del alcance inicial con pruebas y evidencia Native AOT.
- [ ] Probar regresiones del recorrido M1 y aceptación del segundo ejecutable.

Esta lista se subdivide después de estabilizar M1; no desarrollar piezas sin dependencias
ni duplicar sus tareas de persistencia, transferencia o ejecución ya terminadas.

## Alcance futuro que requiere aclaración

El recorte de M1 también mencionaba TUI, HTTP propio, autenticación HF, fuentes de terceros
y multiplataforma. Antes de esta planificación se habían aplazado explícitamente.
No asumir que pasan todos a M2 por agruparlos en un párrafo de exclusiones: confirmar
con el usuario si su inclusión cambia ese alcance. La TUI estaba prevista después del
milestone de todas las funciones iniciales; no elegir su librería ni anticiparla.

## Aceptación propuesta

- [ ] Segundo ejecutable publicado Native AOT, con funciones aceptadas y sin comandos ficticios.
- [ ] Recorrido M1 sigue funcionando.
- [ ] Actualizaciones conservan identidad, propiedad, revisión fijada y recuperación acordadas.
- [ ] Externos no reciben actualizaciones ni eliminación física.
- [ ] Pruebas funcionales y de fallos, más publicación/ejecución nativa verificadas.
