---
status: planned
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.119314200Z
updated_at: 2026-10-10T16:05:40.237079200Z
transitions:
- status: planned
  at: 2026-10-10T16:05:40.237079200Z
---
# Gestión completa del catálogo de parámetros personalizados

## Objetivo
Permitir crear, consultar, editar y eliminar definiciones mediante param, incluida su
reasignación a otro binario y el ajuste de versiones soportadas.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#param)
- [Gestión del catálogo](../../docs/catalogo-de-parametros.md#gestión-de-parámetros-personalizados)

## Alcance
Implementar la gestión local de definiciones, su persistencia y su aplicación al componer
una ejecución, conforme al catálogo funcional. Incluye creación, consulta, edición,
reasignación y eliminación; la importación/exportación tiene una entrega separada.

## Aceptación
- [ ] Crear una definición oficial nueva y otra específica de un binario no oficial.
- [ ] Verificar la separación define/edit: rechazar creación duplicada y edición de
  una definición inexistente sin cambios; conservar campos omitidos durante la edición.
- [ ] Consultar y listar sus metadatos y aplicabilidad.
- [ ] Editar y eliminar definiciones iniciales y creadas por el usuario; verificar
  la protección de las definiciones host, port y metrics.
- [ ] Verificar el diagnóstico de usos y los recorridos de confirmación, cancelación y
  falta de entrada interactiva de la [eliminación](../../docs/catalogo-de-parametros.md#eliminación-de-definiciones).
- [ ] Comprobar la cascada en varios presets y servidores, preservando los recursos
  referenciados, y la consistencia del estado ante fallos de persistencia.
- [ ] Editar metadatos y reasignar el binario sin duplicar la definición ni borrar presets.
- [ ] Probar cambios de tipo y enumeración con asignaciones compatibles e incompatibles,
  confirmación, cancelación y fallos conforme a la
  [edición del catálogo](../../docs/catalogo-de-parametros.md#edición-con-asignaciones-incompatibles).
- [ ] Validar enumeraciones y defaults contra el tipo declarado.
- [ ] Verificar las opciones de tipo, descripción, default, alias, valores enumerados,
  ámbito y destino de define/edit según la [CLI](../../docs/comandos.md#param).
- [ ] Verificar opciones repetidas para alias y enumeraciones: reemplazo de la lista
  durante edit, conservación al omitirla y alias que comienzan con guiones.
- [ ] Verificar la limpieza explícita de default, descripción y alias; rechazar opciones
  contradictorias y definiciones resultantes con enumeración vacía o default incompatible.
- [ ] Verificar que editar el default no modifica asignaciones existentes y que retirar
  una asignación no lo repone, conforme a los
  [defaults para formularios](../../docs/catalogo-de-parametros.md#valores-predeterminados-para-formularios).
- [ ] Verificar la [consulta del impacto](../../docs/catalogo-de-parametros.md#consulta-del-impacto)
  en edición y eliminación, sin escrituras ni interacción, incluida la salida a archivo.
- [ ] Comparar el informe con la operación real sobre el mismo estado; comprobar que
  un cambio posterior del catálogo o de las asignaciones obliga a recalcular el impacto.
- [ ] Verificar enteros, fracciones y booleanos según los
  [tipos documentados](../../docs/catalogo-de-parametros.md#tipos-y-evidencia-del-backend),
  sin inferir el tipo a partir del valor asignado.
- [ ] Comprobar asignación al ámbito correcto: parámetros de modelo en presets y
  parámetros de proceso en servidores; guardar definiciones no asigna valores.
- [ ] Probar rangos con ambos límites, un solo límite y sin límites; comprobar inclusión
  de los extremos, exclusión de versiones exteriores y versión exacta con límites iguales.
- [ ] Verificar los [esquemas de versiones](../../docs/catalogo-de-parametros.md#comparación-de-versiones):
  orden numérico de builds, SemVer y etiquetas exactas; rechazar rangos invertidos,
  versiones inválidas y rangos sobre etiquetas sin orden.
- [ ] Guardar un valor en un preset; incluirlo con backend/versión compatibles y omitirlo
  con warning en una ejecución fuera de su aplicabilidad, conservando el valor guardado.
- [ ] Cubrir backends de la misma familia con distinta aplicabilidad.
- [ ] Asociar una definición a varios orígenes y familias/variantes, con rangos
  independientes; verificar el alias oficial/ggml y la edición de una asociación
  sin alterar las demás ni duplicar la definición.
- [ ] Definir asociaciones por origen y familia/variante sin binarios instalados;
  rechazar IDs locales inexistentes al crear o editar una asociación y diagnosticar
  referencias locales ausentes sin reasignarlas por nombre.
- [ ] Verificar support set/remove, asociaciones generales sin selector y rangos
  evaluados según el esquema del origen efectivo, incluido el diagnóstico de límites
  que no puedan compararse.
- [ ] Retirar la asociación general sin selector y comprobar que se conservan
  las asociaciones específicas y las asignaciones de valores.
- [ ] Verificar la precedencia backend local, origen/familia y general; comprobar que
  una versión fuera del rango específico no recupera el soporte de una asociación general.
- [ ] Rechazar argumentos/metadatos inválidos sin modificar el estado.
- [ ] Actualizar la ayuda y ejecutar las pruebas de los recorridos anteriores.
- [ ] Publicar y ejecutar Native AOT win-x64; registrar evidencia reproducible.

## Límites
No cambia los valores guardados en presets o servidores de forma implícita ni modifica
binarios. La importación/exportación JSON corresponde a [M3](../../docs/milestones/M3-definiciones-declarativas.md), fuera de M2.
Esta entrega guarda el default como metadato; no implementa formularios de terminal o web.
El restablecimiento de fábrica corresponde a [022](../022-m2-config-param-reset/README.md).
