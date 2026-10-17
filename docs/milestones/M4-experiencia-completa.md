# M4 — Experiencia completa e interfaces visuales

**Estado:** horizonte de alcance, sin implementación iniciada. **Depende de:** [M3](M3-definiciones-declarativas.md).

## Objetivo

Completar las funcionalidades acordadas en el relevamiento sobre recetas, servicios y consumidores de M3, incluida la Web UI y los editores de TUI/Web. Las interfaces usan
los mismos contratos y capacidades de la CLI; no crean catálogos ni ciclos de
ejecución propios. No es una segunda reescritura del producto.

El soporte completo de proyectos con YAML pertenece a M3; M4 no se utiliza para
posponer partes de ese ciclo necesarias para que el proyecto funcione mediante CLI.

## Experiencia a completar

- Ampliar los dashboards TUI entregados en M3 con acciones de control; Web UI con estado, logs, métricas, gráficos y notificaciones accionables.
- Experiencia avanzada de edición en TUI/Web UI: editores/formularios de proyecto, presets y argumentos, aprovechando schemas
  cuando existan y conservando edición libre donde corresponda.
- Borradores recuperables por sesión, conflictos con cambios externos, guardado
  explícito y creación de overrides de diferencias; separación entre archivos
  compartidos y estado local ignorado por Git.
- Selección de YAML y comparación visual de configuración elegida frente a la
  realmente activa, sin cambios implícitos sobre el servidor.
- Flujo visual de exploración de modelos, lectura de información y descarga,
  creación/prueba de presets en un router vivo y persistencia de la receta útil.
  Incluye el alta/matching de archivos locales sobre el soporte de proyecto de M3;
  no difiere a esta entrega la capacidad del núcleo para reutilizarlos.
- Paridad de acciones de instancia y recuperación, observación por eventos y
  reconexión; cerrar interfaces no detiene servidores desacoplados.

## Cobertura restante y entregas

M4 completa el alcance acordado que no se entregue en M3. La planificación de
tarjetas debe mantener trazabilidad de las funcionalidades para que no se pierdan
al dividir el trabajo; una pantalla no acredita por sí sola un flujo completo.

El benchmarking local de rendimiento y consumo forma parte de la visión acordada.
Al descomponer M3/M4 se ubicará su primera entrega; M4 cierra la cobertura restante.
No se impone como condición de aceptación de M3 por este documento. Se reutiliza
el procedimiento de peticiones/tokens/timings estudiado, sin exigir /metrics para
tasas básicas. RAM/VRAM requiere instrumentación comprobada y compatible con
Native AOT; no se exige concurrencia de gran escala en la primera aproximación.

La calculadora estimativa de recursos permanece como posibilidad futura, no como
requisito automático de M4. Navegación entre proyectos tipo IDE no debe convertirse
en un IDE completo. «Completar las features» significa el alcance acordado y
descompuesto, no incorporar indefinidamente cualquier idea futura.

## Preparación y aceptación

- Concretar flujos y diseño visual revisables sobre contratos reconciliados,
  sin otro cuestionario abierto para detalles rutinarios.
- Verificar búsqueda/descarga, edición, prueba, guardado y reproducción como
  recorridos completos, incluyendo fallos y recuperación.
- Mantener el servidor desacoplado y diferenciar estado observado, borradores,
  intención guardada y composición aplicada.
- Comprobar dependencias visuales y telemetría en el camino Native AOT antes de
  adoptarlas, con publicación y ejecución para los destinos acordados.
- No presumir métricas, eventos o recarga en cualquier fork. La Web UI requiere
  concretar acceso y despliegue antes de implementarse; esta inclusión no acredita
  hoy un servicio web disponible.
- Crear tarjetas al acordar entregas; este horizonte no crea tareas ni autoriza
  paquetes, código o commits por sí mismo.

La edición asistida no es una dependencia de M3. M3 permite usar un editor externo y validar/componer YAML, además de comandos puntuales que persisten asignaciones. M4 utiliza esos contratos para edición visual, sin exigir construir un editor de texto generalista ni un IDE completo.

La prioridad de edición es combinar servidor, modelos y presets en TUI/Web UI.
El buscador y los formularios de argumentos/parámetros se ordenan después de ese
recorrido. El catálogo y la validación declarativa ya existen como contrato de M3;
la experiencia avanzada de autoría de parámetros no bloquea su aceptación y su
inclusión concreta en M4 se decide al descomponer esa experiencia.
