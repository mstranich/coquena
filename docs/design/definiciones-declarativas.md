# Definiciones declarativas

El contrato concreto se describe en [Formato del proyecto](formato-proyecto.md).

## Proyecto y definición

Una carpeta con `coquena.yaml` contiene una receta de servicio, consumidor o ambos.
El servicio define un backend efectivo; el consumidor publica sus presets en el
target seleccionado. Distintas combinaciones especializan la intención de la
carpeta; no crean procesos propios para cada consumidor de un servicio compartido.
El name de raíz identifica al único servicio opcional de la receta, según
[servicios y recetas](servicios-recetas.md).

El estado local del proyecto registra la referencia de cada receta y el nombre de receta
con el que se dio de alta. Servicios y presets mantienen sus nombres registrados
hasta baja y alta explícitas, aunque cambie el nombre de la carpeta o la intención
YAML. Resolver esa referencia exige distinguir archivos homónimos; no se asocia
cualquier `coquena.yaml` por su basename. La home sólo indexa su ubicación conocida.
Véase [registro](persistencia-proyectos.md#registro-local-de-recetas).
La definición se escribe a mano o por CLI. La CLI verifica el YAML antes de editar
y el resultado antes de publicarlo. Sintaxis, esquema estructural y configuración
requerida se validan siempre; aceptar la definición no certifica inferencia.

Init crea archivos básicos y un YAML casi vacío o con defaults mínimos, sin descargar
ni iniciar. Prepara .gitignore sin reemplazar reglas existentes: descargas, estado
local, logs locales y .env no se versionan por defecto. No hace git init implícito.

## Recursos y archivos

Backend, modelos y companions gestionados pertenecen al ámbito que los prepara:
proyecto o global importado. Consumir un servicio no transfiere sus recursos.
Los externos se referencian explícitamente y siguen bajo gestión del usuario.
Incluye archivos ya obtenidos con otra herramienta y copiados o movidos por el
usuario al proyecto: se registran allí y pueden usarse sin nueva descarga.
Una ubicación dentro de la carpeta no cambia automáticamente su propiedad.
Las referencias locales interiores son relativas a la base del proyecto;
una referencia exterior sigue dependiendo de esa ruta y no se vuelve portable sola.
Recibir la receta y ejecutar Coquena prepara lo faltante y reutiliza recursos
válidos existentes. Perder la home no obliga por sí solo a redescargarlos.

La carpeta de metadatos del proyecto separa compartible y local con .gitignore,
también interno. El layout se concreta en [Formato del proyecto](formato-proyecto.md); la coordinación se especifica en [persistencia](persistencia-proyectos.md). No implica repositorio Git anidado ni sidecar por shard.

| Información | Función y portabilidad |
| --- | --- |
| YAML y schema de argumentos | Intención/reglas compartibles |
| Locks por combinación | Resoluciones e integridad de referencia compartibles |
| Inventario y operaciones | Estado local, rutas, propiedad, motivo de descarga y progreso |
| Instancia y última buena | Snapshot y comprobación de esta máquina; no se importan como éxito |
| Borradores por sesión | Recuperables y locales, fuera de Git |
| Logs | Diagnóstico, no lock; destino configurable |
| Secretos interpolados | No persistir en locks compartidos ni exponer en informes |

No se exige almacén compartido ni deduplicación entre proyectos. La home contiene
preferencias, registro de rutas/servicios y el ámbito global importado explícitamente.
Config administra preferencias; import incorpora intención. Un proyecto autónomo
no exige importar nada a la home. Referencias se resuelven dentro del ámbito
seleccionado, sin búsqueda por nombres en otros proyectos.

## Combinación y rutas

Seleccionar explícitamente YAML/lista ordenada. No detectar overrides por SO/máquina.
El último prevalece para el mismo valor. Presets por nombre admiten modificación
parcial de parámetros, modelo y companions. Omisión conserva lo heredado;
clave sin valor desasigna, sin borrar archivos.

Las rutas de todos los archivos parten de la carpeta del proyecto, no de cada override.
La referencia de selección es Compose: -f/--file repetible, base en el primer YAML
y posibilidad de elegir base del proyecto explícitamente. No importa otras funciones
de Compose por semejanza.

Sin selección explícita, CLI edita solo el YAML predeterminado; con lista, solo el
último. El nombre es `coquena.yaml`; la composición se concreta en [Formato del proyecto](formato-proyecto.md). Consultar configuración efectiva combina/valida sin descargar
ni ejecutar.

## Variables de entorno

YAML permite referencias al entorno del proceso y .env seleccionados explícitamente.
No hay carga automática de .env. Entorno del proceso prevalece sobre .env.
Variable ausente sin default explícito produce diagnóstico antes de preparar/iniciar;
no se reemplaza silenciosamente por cadena vacía.

Sintaxis, valores vacíos y precedencia se concretan en [Formato del proyecto](formato-proyecto.md#capas-e-interpolación).

## Intención, resolución y ejecución

YAML declara intención, incluso latest; lock fija versiones concretas. Intención
sin cambios reutiliza lock sin buscar novedades. Cambiar un recurso resuelve ese
cambio sin actualizar los otros latest. Up prepara cambios explícitos/faltantes
sin exigir update/upgrade solo por editar intención.

Cada combinación registra archivos, orden, intención de referencia y resoluciones.
Alternar combinaciones conserva los registros y resoluciones de las otras; esto
no mantiene publicadas simultáneamente sus contribuciones. Aplicar otra combinación
del mismo proyecto al mismo target reconcilia su conjunto publicado según
[servicios compartidos](servicios-recetas.md#compartir-un-servidor-entre-proyectos).
Una invocación explícita la vuelve
conocida aunque falle por hardware; no la acredita funcional ni crea bloqueo de
integridad. No descubrir otros YAML para actualizarlos o protegerlos por adivinación.

Estado local distingue preparado, activo y última buena. La ejecución conserva
composición y recursos reales aunque se edite YAML después. Solo se promueve a
última buena tras [salud](servidores.md#salud-y-promoción); no por completar descargas.

Update/upgrade modifican plan/resoluciones/archivos, no YAML:
[reglas de actualización](actualizacion-y-limpieza.md). Git comparte recetas/locks,
no copia pesos ni acredita compatibilidad o éxito local.

## Validación

La estructura general se valida siempre. `args-mode` distingue permisivo y estricto; ambos validan definiciones conocidas, según [parámetros](catalogo-de-parametros.md).

Verify diagnostica YAML y existencia/integridad de backend, modelos y recursos
de presets seleccionados. Incluir descargas no utilizadas exige opción explícita.
No descarga/repara ni modifica intención/resoluciones; no tiene --fix. Resultados
por recurso se persisten localmente. Sin hash de referencia se informa falta de
evidencia, no se certifican bytes.

Up valida YAML/esquema/requisitos y fallos conocidos; no hace la auditoría de verify
ni recalcula hashes de gigabytes. Preparar faltantes no es auditar todo el disco.
Corregir YAML basta para revalidarlo al próximo intento, sin exigir verify previo.

## Experimentación sin guardar

Una acción explícita puede aplicar una prueba antes de guardar YAML. El backend
necesita conocer el preset; puede requerir representación operativa local generada
por Coquena. No presuponer una API para subir definiciones completas de presets.

Borrador recuperable, preset publicado, modelo cargado y YAML definitivo son estados
distintos. Véanse [interfaces](interfaces-visuales.md#borradores-y-guardado) y
[recuperación](servidores.md#recuperación-y-fallos).

## Contratos técnicos

La estructura y edición están en [formato](formato-proyecto.md); el metaschema y
emisión, en [parámetros](catalogo-de-parametros.md); identidad y coordinación,
en [persistencia](persistencia-proyectos.md). Ningún schema acredita capacidades del router.
