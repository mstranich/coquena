---
status: planned
created: 2026-10-16
priority: medium
tags:
- m2
- bugfix
created_at: 2026-10-16T20:07:30.689473900Z
updated_at: 2026-10-16T20:07:59.136760700Z
---
# Distinguir drafts MTP de modelos principales con MTP

## Problema y evidencia

En EryriLabs/Ornith-1.5-35B-A3B-BigBang-MTP-GGUF, el selector #mtp:Q8_0
ofreció el principal Ornith-1.5-35B-A3B-BigBang-MTP-Q8_0.gguf (36.9 GB)
y omitió el draft separado mtpdraft-Q8_0.gguf (aproximadamente 2 GB).
Sin filtro de cuantización tampoco apareció ese draft. La selección se canceló:
no se descargó el principal equivocado.

ModelResolution reconoce mtp como token delimitado. El nombre de un principal con
MTP integrado o presente en su denominación puede confundirse con el rol auxiliar,
mientras mtpdraft no coincide. El companion alternativo de a4lg sí pudo instalarse.
Los nombres y el tamaño son pistas, no prueba suficiente de rol o compatibilidad.

## Objetivo y alcance

Corregir la resolución de candidatos principales/companions para reconocer drafts
MTP separados y no certificar un principal como companion sólo por contener MTP.
Reproducir con inventario y cabeceras fijados. Usar evidencia acotada de nombre,
carpeta y cabecera; representar incertidumbre o contradicción cuando no baste.
No resolverlo mediante una excepción para un publicador ni por un umbral de tamaño.

Si las cabeceras no permiten distinguir un caso, documentar la evidencia disponible
y acordar el tratamiento antes de inventar una garantía. Mantener selección explícita,
commit fijado, unidades y shards completos y ausencia de operaciones antes de confirmar.
No incorporar nuevos tipos de companion, convertir checkpoints ni certificar
compatibilidad de inferencia entre arquitecturas.

## Referencias

- [M2](../../docs/milestones/M2-gestion-completa.md).
- [Modelos y companions](../../docs/modelos-y-companions.md).
- [CLI](../../docs/comandos.md), [evidencia Ornith](../035-m2-entrega-integrada/prueba-ornith.md).
- Código: src/Coquena/Models/ModelResolution.cs y consumidores de selección.
- Inventario EryriLabs observado en commit 2e9f6f487b82200f2a045e67d01a3f1e2982b00a.
- Antecedentes completos: 015 y 027. Sin dependencia bloqueante con las otras correcciones.

## Trabajo y aceptación

- [ ] Fixture reproduce la omisión de mtpdraft-Q8_0 y el falso candidato principal.
- [ ] El draft separado aparece como candidato auxiliar con cuantización y evidencia
  justificadas; un principal con MTP no se ofrece como companion confirmado por su nombre.
- [ ] Cubrir MTP integrado, separado, nombres arbitrarios, cabecera insuficiente,
  contradicciones, variantes de cuantización y shards; preservar mmproj y principales.
- [ ] Conservar límites de red/metadatos, selecciones explícitas y cancelación sin GUID.
- [ ] Probar selección e instalación del companion mediante CLI Native AOT win-x64
  con fixtures reproducibles y sin descargar los 36.9 GB del principal.
- [ ] Actualizar documentación sólo si cambia o se aclara una regla, señalando cuál;
  no presentar clasificación como certificación de aceleración.
- [ ] Registrar resultados y presentar para aprobación; sin implementación, cierre,
  commit o publicación autorizados por la mera creación de esta tarjeta.
