---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:06:38.360310900Z
updated_at: 2026-11-11T18:05:14.019591900Z
completed_at: 2026-11-11T18:05:14.019591900Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.718423500Z
- status: planned
  at: 2026-11-11T14:52:20.697070100Z
- status: in-progress
  at: 2026-11-11T16:43:42.646429800Z
- status: complete
  at: 2026-11-11T18:05:14.019591900Z
---
# Descargas como pestaña única y categorizada

## Objetivo

Descargas pasa a ser un único lugar para las transferencias de la sesión: una pestaña
que abre el indicador `⇣` de la barra superior, con las operaciones de Global y del
proyecto actual agrupadas y ordenadas. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

Cada ámbito tiene su página Descargas dentro de Definiciones; el indicador obliga a
elegir ámbito y el usuario debe saber dónde se inició una transferencia
([hallazgo 010](../../docs/findings/010-descargas-globales.md)). Navegar a una página y
volver pierde la vista de origen ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).
Una descarga lanzada desde «+ Backend» no ofreció Cancelar
([hallazgo 011](../../docs/findings/011-cancelar-descarga-tui.md)).

## Comportamiento esperado

- `⇣` es una pestaña, como En vivo, Definiciones y Configuración: se abre con clic o
  con la navegación entre pestañas, sin atajo propio; se resalta cuando está activa y
  sigue mostrando el progreso cuando no. Cada pestaña abre siempre en su estado anterior
  (vista, selección, desplazamiento y foco). No existe «Volver».
- La barra inferior de recursos queda visible y viva, como en las demás pestañas.
- Una sola lista con las transferencias de Global y del proyecto actual; los demás
  proyectos conocidos no se listan ([hallazgo 015](../../docs/findings/015-descargas-otros-proyectos.md)).
- Agrupación primero por **ámbito** y dentro por **categoría**, en el orden backends,
  modelos y auxiliares (companions), con encabezado y contador por grupo. Todos los grupos están
  desplegados; un ámbito o categoría sin transferencias se oculta.
- Orden por estado: descargando, pausadas y, al final, erróneas; dentro de un mismo
  estado, por hora de inicio, de la más reciente a la más antigua. Cada operación guarda
  su fecha de creación; las creadas antes usan la fecha de creación de su carpeta.
- Visibilidad igual que en la CLI: sólo operaciones en progreso o detenidas (pausadas
  o erróneas); las terminadas no quedan a la vista ni requieren limpieza.
- Cada fila indica recurso, avance, velocidad o estado y sus acciones; el indicador `⇣`
  suma la velocidad total. Acciones: Pausar,
  Reanudar, Cancelar y Reintentar según corresponda. Toda operación activa o detenida
  ofrece Cancelar, incluidas las de la preparación de un backend, con el mismo efecto
  que `download cancel` en su ámbito.
- Un ámbito inaccesible o con estado inválido se informa sin ocultar el otro.
- Descargas deja de ser un nodo del árbol de Definiciones.
- La regla de [descargas](../../docs/design/descargas.md) no cambia: cada operación
  pertenece a su ámbito; sólo cambia la presentación.

## Por concretar

- [x] Wireframe N según la [vista previa](../../docs/design/tui/vista-previa.html) y la regla
      de [pantallas](../../docs/design/tui/README.md).
- [x] Reproducir el caso de 011 (Cancelar en la preparación lanzada desde «+ Backend»).

## Criterios de aceptación (borrador)

- [x] Cambiar a Descargas y volver a cualquier pestaña conserva su estado; no hay «Volver».
- [x] La barra inferior queda visible y viva en la pestaña.
- [x] Las transferencias de Global y del proyecto actual aparecen agrupadas y ordenadas
      según lo acordado; las acciones actúan sobre la operación y el ámbito correctos.
- [x] Cancelar funciona en descargas iniciadas desde cualquier recorrido y equivale a
      `download cancel`.
- [ ] Clic y navegación por teclado; fidelidad al boceto a 80×24 y en terminal mayor.
      Comprobado con clic a 80×24. Teclado, terminal mayor y una fila con error real no se
      verificaron a mano; el usuario aprobó cerrar la tarjeta con esa limitación.
- [x] Pruebas con ambos ámbitos, publicación Native AOT y revisión visual.

## Evidencia

| Verificación | Resultado |
|---|---|
| `--dashboard-tests` | 223 comprobaciones; agrupación por ámbito y categoría, orden por estado y por inicio, ocultas las terminadas y canceladas, acciones con ámbito y operación, indicador `⇣` y pestaña resaltada |
| Suite completa | Se detiene en `HelpTests` («help fits 80 columns») por la línea de `config inference-history`, ajena a esta entrega; no se ejecutaron las pruebas posteriores |
| Publicación win-x64 Native AOT | Sin advertencias |
| Binario a 80×24 con `COQUENA_HOME` aislado | Global y carpeta actual con una descarga pausada cada una; Reanudar mostró velocidad, tiempo restante y `⇣ 1 · 80%`; la terminada salió de la lista; Cancelar con confirmación retiró la operación de Global (`download list` vacío); Definiciones sin nodo Descargas y con su selección al volver |
| Contratos | `createdAt` opcional: round-trip con y sin el campo; una fecha vacía se rechaza |
| Binario con descargas nuevas | Global y carpeta actual guardaron `createdAt` en `operation.json`; la pestaña mostró «2 pausadas», «work (esta carpeta)», `⇣ 1 · 30% · 16M/s` al reanudar y el modal «Se borrará lo descargado hasta ahora.»; Cancelar retiró la de Global |
| Caso 011 en el binario a 80×24 | «+ Backend» de Global con `cuda@13` y `cuda@12`: la preparación apareció en BACKENDS con Pausar y Cancelar y el indicador `⇣`. Cancelar al 11 % dejó `download --global list` vacío, sin parciales y sin instalar `cuda@12`. Si la descarga termina con la confirmación abierta, ahora se informa que ya había terminado en lugar de «Operación inexistente.» |
| Cancelación durante una preparación | Motor: si la transferencia se cancela, la preparación de backend o de modelo termina con `transfer-cancelled` («Descarga cancelada; no se instaló el backend.») en lugar de buscar una instalación inexistente; `--scoped-backend-tests` (51) lo cubre con una cancelación a mitad de la transferencia. En el binario, cancelar `cuda@12` desde «+ Backend» al 22 % dejó el aviso ▲ en Notificaciones y ningún error |

## Exclusiones

- El hallazgo 009 (cambiar el backend de un servicio). Los cambios en el motor, autorizados
  por el usuario, son la fecha de creación opcional de la operación y el resultado
  `transfer-cancelled` cuando se cancela una preparación.
- Listar o navegar a otros proyectos conocidos (hallazgo 015).
