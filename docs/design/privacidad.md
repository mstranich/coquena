# Privacidad y datos compartibles

## Información local y publicación

La operación local necesita identificar recursos, procesos y dispositivos reales.
El diagnóstico y la observación local pueden mostrar rutas, nombres de dispositivos
y capacidades de hardware cuando son necesarios para explicar el estado o un fallo.
Conservar esos datos localmente no autoriza incorporarlos a una publicación.

Los documentos compartibles, informes, ejemplos, logs y capturas destinados a
publicación excluyen datos personales y del equipo utilizado. Esta regla también
se aplica a la evidencia versionada del proyecto. Una salida local o un log crudo
no se considera compartible sólo por haberse generado correctamente.

## Contenido compartible

No se publican nombres de usuario local o de equipo, correo personal, directorios
de trabajo reales, identificadores de dispositivos, direcciones de red privadas
ni el perfil concreto de CPU, GPU, RAM o VRAM del equipo usado. Credenciales,
tokens y URLs firmadas tampoco forman parte de diagnósticos compartibles.
Las expansiones de variables siguen además las reglas de
[interpolación](formato-proyecto.md#capas-e-interpolación).

Las rutas de reproducción se expresan con variables o parámetros definidos en el
propio ejemplo, como `$TestRoot` y `$CoquenaHome`. Las ubicaciones originales se
conservan sólo en registros locales o intercambios privados, fuera del contenido
publicado. Parametrizar mantiene el significado del recorrido: rutas distintas,
ámbitos y diferencias de capitalización relevantes no se convierten en una única
ruta indistinguible.

Se conservan los conceptos técnicos necesarios: plataforma soportada, versiones
del backend, selección de recursos, argumentos, fuente, unidad y alcance de las
mediciones. Mencionar CPU/GPU o compatibilidad CUDA/Vulkan no identifica por sí
solo el equipo del autor. Los dispositivos y cifras usados en ilustraciones se
presentan como ejemplos, sin atribuirlos al entorno real ni acreditarlos como
mediciones.

## Resultados de entornos anonimizados

Un resultado publicado indica si el entorno de prueba fue anonimizado. Conserva
el resultado observado, sus errores, condiciones conocidas y límites, sin inventar
un hardware sustituto ni presentar la ejecución como medida sobre el equipo de
quien la reproduce.

Si se omiten datos necesarios para comparar capacidad o rendimiento, se declara
esa limitación. El resultado no garantiza el mismo rendimiento en otro equipo;
anonimizar no convierte una medición en un requisito mínimo o una estimación
universal. El registro local puede conservar el detalle necesario para analizar
la ejecución original, según las reglas de [benchmarking](benchmarking.md).

## Autoría y conservación

La autoría y los créditos públicos conservan el nombre del autor. El correo
personal se excluye de la publicación; los metadatos Git del autor utilizan su
correo noreply de GitHub. El nombre de autoría no autoriza divulgar su usuario
local, nombre de equipo ni datos de contacto personales.

Excluir un archivo mediante `.gitignore` no retira contenido ya versionado ni lo
vuelve secreto. La separación entre información local y compartible debe respetarse
también en archivos ya rastreados y al conservar evidencia histórica. Copias de
respaldo con información original permanecen privadas.
