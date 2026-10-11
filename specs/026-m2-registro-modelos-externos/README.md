---
status: planned
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:43.321536100Z
updated_at: 2026-10-10T16:48:26.470881300Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.470881300Z
---
# Registrar modelos y companions externos

## Objetivo
Incorporar archivos locales al catálogo sin asumir propiedad sobre ellos ni exigir acceso
remoto.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#incorporación-externo-o-gestionado)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [ ] Implementar model add y companion add con nombre canónico, ID estable y propiedad
  externa, sin copiar/mover/adoptar archivos.
- [ ] Probar archivos y shards estándar, colisiones de nombres/rutas, registro incompleto e
  inaccesible y contradicciones GGUF; no servir unidades incompletas.
- [ ] Listar disponibilidad sin red ni hash completo; conservar referencias por ID y
  diagnosticar cambios externos sin reparación.
- [ ] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
Emparejamiento corresponde a 029; refresh/integridad a 028 y retirada a 034.
No copia ni adopta archivos externos.
