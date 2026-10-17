---
status: planned
created: 2026-10-21
priority: medium
tags:
- m3
depends_on:
- 041-ayuda-cli
- 043-descarga-directa
- 056-m3-importacion
- 060-m3-limpieza
- 062-m3-seguimiento-logs
- 064-m3-observacion-continua
- 071-m3-edicion-cli
- 072-m3-init
- 070-m3-dashboard-integrado
created_at: 2026-10-21T23:26:07.360340300Z
updated_at: 2026-10-21T23:26:07.360340300Z
---

# Completar cobertura y ayuda de la CLI del milestone

## Objetivo

Acreditar coherencia de las familias CLI entregadas por M3, sin convertir la auditoría en implementación de dominios pendientes.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [comandos.md](../../docs/comandos.md)
- [servicios-recetas.md](../../docs/servicios-recetas.md)
- [formato-proyecto.md](../../docs/formato-proyecto.md)
- [glosario.md](../../docs/glosario.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [ ] Mantener matriz de cada comando/opción y tarjeta que lo entrega, con ámbito, efecto, selección y códigos de salida; comprobar que ningún comando requerido quede sin responsable.
- [ ] Integrar ayuda real de operaciones implementadas, aliases service/server y backend/runtime, flags ps -w/--watch y opciones locales; no inventar versiones, consultas o abreviaturas no definidas.
- [ ] Comprobar config sin proyecto, contexto global explícito, target default/self/nombrado, GUID aislados y ausencia de efectos por consultas.
- [ ] Cubrir import selectivo y reemplazo, ciclo de presets frente a proceso, pin/latest, confirmación de clean y tamaños humanos de escala 1024.
- [ ] Verificar cuantización inequívoca de 043, init opcional y edición puntual sin convertirlos en requisitos del YAML manual.
- [ ] Asignar faltantes de dominio a su tarjeta y comprobar sus pruebas antes de acreditar cobertura; mantener docs funcionales como fuente de verdad.
- [ ] Acreditar entrada tui, navegación general/detalle y disponibilidad real del dashboard integrado; la CLI no presenta la observación visual como una función futura.

## Dependencias

- [041: Organizar la ayuda según la sintaxis real del ejecutable](../041-ayuda-cli/README.md)
- [043: Descargar una cuantización explícita inequívoca sin interacción redundante](../043-descarga-directa/README.md)
- [056: Importar recetas completas o selectivas con reemplazo explícito](../056-m3-importacion/README.md)
- [060: Liberar recursos sólo con propiedad y referencias comprobadas](../060-m3-limpieza/README.md)
- [062: Seguir logs por identidad de servicio entre reinicios y rotación](../062-m3-seguimiento-logs/README.md)
- [064: Observar con ps -w o --watch y reconciliar eventos](../064-m3-observacion-continua/README.md)
- [071: Persistir cambios puntuales de entidades en el YAML seleccionado](../071-m3-edicion-cli/README.md)
- [072: Inicializar una receta mínima sin fabricar estado de ejecución](../072-m3-init/README.md)
- [070: Integrar y acreditar el dashboard Servicios completo](../070-m3-dashboard-integrado/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin funciones M4, comandos nuevos por analogía, reglas alternativas a los canónicos ni cambios de dominio para hacer pasar una matriz.

## Estado y evidencia

Planificada, sin implementación ni pruebas ejecutadas. No se acredita funcionalidad por redactar esta tarjeta; inicio, cierre y commit requieren su autorización correspondiente.
