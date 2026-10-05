# Actividades complementarias

Los flujos de modelos y descargas se describen en [secuencias.md](secuencias.md).
Aquí sólo se conservan actividades complementarias y borradores de ejecución.

## Selección e instalación de backend

```plantuml
@startuml
start
:Usuario pide instalar\n(familia y build opcionales);
:Filtrar por SO y arquitectura nativa;
if (¿indicó familia?) then (sí)
  :Filtrar por familia;
endif
if (¿indicó build?) then (sí)
  :Filtrar por ese build;
endif
:Evaluar compatibilidad\ncon el hardware;
if (¿se indicó familia concreta?) then (sí)
  :Conservar candidatos seleccionados\naunque sean incompatibles o unknown;
  :Informar estado y motivo;
else (no)
  :Conservar sólo compatibles;
endif
if (¿quedan candidatos?) then (no)
  :Informar por qué no hay candidato;
  stop
endif
:Elegir el build más reciente\nentre los candidatos;
if (¿hay varios candidatos\nen ese build?) then (sí)
  :Aplicar preferencia de backend\n(CUDA compatible más alta en NVIDIA,\nluego Vulkan; CPU fallback);
endif
if (¿no indicó familia?) then (sí)
  :Informar elección automática\ny listar alternativas;
endif
if (¿la familia requiere dependencias?\n(p. ej. cudart en Windows)) then (sí)
  :Sumar las dependencias\ndel mismo release;
endif
if (¿ya está instalada?) then (sí)
  :Informar y terminar;
  stop
endif
:Descargar;
:Verificar artefactos y extraer en preparación;
:Sondear llama-server --version;
if (¿responde?) then (sí)
  :Asignar ID único y registrar\nel backend instalado;
else (no)
  :Descartar la instalación;
  :Informar el error;
endif
stop
@enduml
```

**Sólo se descarga para instalar y usar.** Un binario de otro SO o arquitectura se
rechaza incluso si el usuario lo selecciona explícitamente. La elección explícita
permite continuar ante aceleración `unknown` o `incompatible` en la plataforma
correcta, pero la instalación aún exige que `llama-server --version` funcione
localmente. Coquena no ofrece descargas sin instalación.

## Registrar un backend externo

```plantuml
@startuml
start
:Usuario indica un llama-server externo\ny declara su familia;
if (¿ruta existente y ejecutable?) then (no)
  :Rechazar sin registrar;
  stop
endif
:Ejecutar llama-server --version;
if (¿termina correctamente y\nla versión es reconocible?) then (no)
  :Rechazar con diagnóstico;
  stop
endif
:Asignar ID único y registrar ruta,\nfamilia declarada, versión sondeada\ny origen externo sin mover archivos;
stop
@enduml
```

El sondeo valida el ejecutable, no demuestra compatibilidad con una GPU, no verifica
la familia declarada ni hace una prueba de inferencia. Coquena no infiere la familia
del nombre de la carpeta. Al retirarlo sólo se borra su registro.

## Iniciar una instancia (borrador)

Este flujo requiere revisión con el usuario; omisión de presets, modos y limpieza
no son reglas cerradas.

```plantuml
@startuml
start
:Usuario pide iniciar un servidor\n(backend, puerto y modo opcionales);
if (¿indicó ID de backend?) then (sí)
  :Resolver ese backend exacto;
else (no)
  if (¿indicó familia?) then (sí)
    :Filtrar por la familia indicada;
  elseif (¿el servidor fija familia?) then (sí)
    :Filtrar por la familia del servidor;
  endif
endif
if (¿indicó build?) then (sí)
  :Filtrar por ese build;
endif
:Filtrar por SO y arquitectura nativa;
:Inspeccionar hardware y evaluar\ncompatibilidad de los instalados;
if (¿backend elegido explícitamente?) then (sí)
  :Conservarlo aunque su aceleración\nsea unknown o incompatible;
  :Informar estado y motivo;
else (no)
  :Conservar sólo compatibles\nde la distribución oficial;
endif
if (¿hay candidatos?) then (no)
  :Rechazar y sugerir\nbackend install;
  stop
endif
if (¿se indicó ID exacto?) then (no)
  :Elegir el build más reciente\nentre los candidatos;
  if (¿hay varios candidatos\nen ese build?) then (sí)
    :Aplicar preferencia de backend\n(CUDA compatible más alta en NVIDIA,\nluego Vulkan; CPU fallback);
  endif
endif
if (¿no se fijó backend ni familia?) then (sí)
  :Informar elección automática\ny listar alternativas;
endif
if (¿el puerto está libre?) then (no)
  :Rechazar indicando qué lo ocupa;
  stop
endif
fork
  :Aplanar eje backend\n(global → familia)\nen [*];
fork again
  :Por cada preset,\naplanar eje modelo\n(modelo → preset)\nen su sección;
end fork
:Verificar modelos y companions\nde cada preset en disco;
if (¿algún preset tiene archivos\nfaltantes o inaccesibles?) then (sí)
  :Avisar cuáles y omitir\nesos presets;
endif
if (¿queda al menos un preset\nalcanzable?) then (no)
  :Rechazar: no hay\nningún modelo para servir;
  stop
endif
:Evaluar parámetros contra\nel catálogo de la versión;
if (¿hay parámetros no admitidos\no desconocidos?) then (sí)
  :Avisar (consultivo);
endif
:Asignar ID de instancia;
:Escribir el INI en\n<home>/run/<servidor>/<id>.ini;
:Lanzar llama-server\n--models-preset <INI> + puerto;
:Guardar ID, puerto, backend\ny PID junto al INI;
:Esperar /health;
if (¿responde a tiempo?) then (sí)
  if (¿modo daemon?) then (sí)
    :Desacoplar y devolver la terminal;
  else (no)
    :Seguir logs en la terminal\nhasta Ctrl+C;
    :Detener la instancia;
  endif
else (no)
  :Marcar Fallida;
  :Mostrar el final del log;
endif
stop
@enduml
```

**Dónde vive el INI generado.** En el home de Coquena, no en la carpeta temporal del
sistema operativo: un limpiador podría borrarlo mientras `llama-server` sigue corriendo, y
Coquena no queda en ejecución para regenerarlo. Cada lanzamiento usa su propio archivo
(p. ej. `run/<servidor>/<id>.ini`) para que dos instancias no se pisen, y se borra al
detener la instancia.

## Detener una instancia (borrador)

```plantuml
@startuml
start
:Resolver ID de instancia\ny leer PID guardado;
if (¿el proceso existe y es el\nllama-server que se lanzó?) then (no)
  :Informar Huérfana\ny limpiar PID;
  stop
endif
:Pedir terminación ordenada;
if (¿terminó a tiempo?) then (no)
  :Forzar terminación;
endif
:Borrar PID e INI generado;
stop
@enduml
```

## Clonar un preset

```plantuml
@startuml
start
:Usuario elige un preset origen\ny un modelo destino;
:Copiar los valores propios del preset\n(no los heredados);
note right
  Incluye referencias propias a companions,
  no copia sus archivos ni valores heredados.
  El clon es independiente del preset origen.
end note
:Pedir nombre nuevo;
if (¿nombre válido y libre?) then (no)
  :Rechazar;
  stop
endif
:Resolver referencias de companions\nde la configuración efectiva del destino;
:Guardar el preset\nbajo la configuración del modelo destino;
note right
  Hereda la configuración del destino.
  Cambios posteriores del preset origen
  no afectan al clon.
end note
stop
@enduml
```


## Referenciar un companion desde la configuración

```plantuml
@startuml
start
:config set <param> --ref <alias>\n[ámbito];
:Resolver la referencia sin\nconfundirla con una ruta literal;
if (¿identifica un único registro?) then (no)
  :Rechazar con diagnóstico;
  stop
endif
:Guardar el valor de configuración\ncomo referencia, no como ruta resuelta;
:Al lanzar, resolver herencia y\ncomprobar archivos efectivos;
:Emitir las rutas absolutas en el INI;
stop
@enduml
```

Registrar mediante `model companion add` no configura ni activa una funcionalidad.
CPU/GPU y los ajustes de inferencia se expresan con parámetros normales del backend.
La compatibilidad funcional se delega al backend al cargar la combinación; no hay
validación ni modo forzado en Coquena para ella. Coquena conserva sus comprobaciones de
referencias y disponibilidad, sin garantizar funcionamiento. `--ref` distingue una
referencia por alias de un valor literal; no se infiere por el contenido del texto.
