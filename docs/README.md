# Coquena — documentación funcional

Coquena prepara y opera servicios locales de IA mediante recetas reproducibles.
Una receta define un servicio con su backend, presets consumidores de un servicio,
o ambos. El usuario puede experimentar sobre un router vivo, observar resultados
y conservar lo útil como una receta que puede llevar a otra máquina compatible.

El estado local en `.coquena/` conserva la referencia y nombre de receta usados al cargar
la receta. La home mantiene un índice de localización. Los nombres
de servicios y presets creados permanecen hasta su baja y nueva alta explícitas;
renombrar la carpeta no los cambia.

El YAML expresa intención; los locks conservan resoluciones concretas. CLI, TUI y
Web UI comparten operaciones. La CLI permite editar la definición con coherencia;
las interfaces visuales facilitan configurar y observar.

## El contexto es el proyecto

Los recursos gestionados pertenecen al ámbito donde se preparan: `.coquena/` del
proyecto o `COQUENA_HOME` tras importación global explícita. Los externos se usan
en su ubicación y conservan su propiedad. No se exige deduplicación ni un almacén
global para consumir servicios compartidos. Conservar un proyecto autónomo permite
reutilizar sus recursos sin depender de una home anterior.

Hay una instancia activa como máximo por servicio. Un proyecto puede consumir un
servicio global sin tener un proceso propio. Copias del proyecto pueden
funcionar simultáneamente si no colisionan recursos. El hardware condiciona la
ejecución; no sustituye la intención ni autoriza cambiar automáticamente un backend
incompatible por otra familia.

## Recorrido de uso

1. Escribir o recibir una receta; inicializar una carpeta es opcional.
2. Definir servicio o destino, modelos y presets; seleccionar overrides.
3. Iniciar el servicio explícitamente o autorizarlo en up; publicar los presets.
4. Ajustar presets, aplicarlos explícitamente al router compatible y probarlos.
5. Guardar receta/locks, compartirlos y reproducirlos en otra máquina.
6. Actualizar, verificar, recuperar y limpiar conservando las protecciones.

Intención, borrador y composición activa pueden diferir. Guardar no reinicia;
cargar un modelo no publica un preset nuevo; descargarlo de memoria no borra pesos.
Up/down publican/retiran el conjunto de presets de la receta; service start/stop
controlan el proceso. Down no detiene el servicio ni retira entradas ajenas.

## Arquitectura de interacción

C# con publicación Native AOT es una restricción del ejecutable y sus dependencias.
Los contratos funcionales no fijan clases o bibliotecas.

En modo desacoplado Coquena inicia/comprueba y termina; el backend continúa.
No hay supervisor Coquena permanente. TUI, Web UI y watch observan mientras están
abiertos, sin ser propietarios de la vida del servidor.
Un schema describe argumentos; no proporciona por sí mismo las APIs de un fork.

## Documentos

| Documento | Responsabilidad |
| --- | --- |
| [Formato del proyecto](formato-proyecto.md) | Campos de coquena.yaml, composición, índices e inicialización |
| [Servicios y recetas](servicios-recetas.md) | Ámbitos, nombres, consumidores e importación |
| [Persistencia de proyectos](persistencia-proyectos.md) | Documentos, identidad, coordinación y publicación recuperable |
| [Definiciones declarativas](definiciones-declarativas.md) | Proyecto, YAML, variables, locks y estado local |
| [Servidores](servidores.md) | Ciclo, backend, puertos, salud, aplicación y recuperación |
| [Modelos y companions](modelos-y-companions.md) | Selección, identidad, shards, propiedad e integridad |
| [Descargas](descargas.md) | Transferencias, pausa, reanudación y preparación |
| [Parámetros](catalogo-de-parametros.md) | Schema optativo por proyecto y emisión |
| [Actualización y limpieza](actualizacion-y-limpieza.md) | Plan, publicación conjunta y protecciones |
| [Observabilidad](observabilidad.md) | Estado, eventos, métricas, logs y reconexión |
| [Privacidad y datos compartibles](privacidad.md) | Información local, anonimización, evidencia pública y autoría |
| [Interfaces visuales](interfaces-visuales.md) | Panel, borradores y comparación |
| [Pantalla Servicios](ui-servicio.md) | Hardware, actividad, histórico visual y logs de la TUI |
| [Benchmarking](benchmarking.md) | Rendimiento y consumo de recursos |
| [Comandos](comandos.md) | Responsabilidades y superficie CLI |
| [Glosario](glosario.md) | Vocabulario y unidades de tamaño y memoria |
| [Modelo de datos](modelo-de-datos.md) | Relaciones conceptuales |
| [Estados](estados.md) | Ejes y transiciones |
| [Casos de uso](casos-de-uso.md) | Actores y recorrido |
| [Secuencias](secuencias.md) | Colaboración entre operaciones |
| [Actividades](actividades.md) | Decisiones de ejecución y edición |
| [Guía técnica](implementacion-catalogo-modelos.md) | Contratos e invariantes de implementación |

Una regla se mantiene en su documento responsable; diagramas y comandos la enlazan.
Las cuestiones no concretadas se identifican, sin autorizar otra política implícita.

## Planificación y evidencia

El contrato funcional no acredita que el ejecutable publicado ya implemente todo.
Recortes, entregas y evidencia pertenecen a specs/ y milestones.

- [M1: servidor local](milestones/M1-servidor-local.md).
- [M2: gestión completa](milestones/M2-gestion-completa.md).
- [M3: soporte completo de proyectos con YAML](milestones/M3-definiciones-declarativas.md).
- [M4: experiencia completa e interfaces visuales](milestones/M4-experiencia-completa.md).
