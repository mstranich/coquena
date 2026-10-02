# Diagramas de actividades

## Instalar una versión de backend

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
:Descomprimir en el home;
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

## Iniciar una instancia

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
if (¿hay parámetros no admitidos?) then (sí)
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

## Detener una instancia

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

## Registrar un modelo externo

```plantuml
@startuml
start
:Usuario indica un GGUF principal\no un conjunto de shards;
if (¿existe y es GGUF legible?) then (no)
  :Rechazar;
  stop
endif
if (¿es parte de un conjunto de shards?) then (sí)
  :Verificar que estén todos;
endif
if (¿ya está registrado?) then (sí)
  :Informar y terminar;
  stop
endif
:Leer metadatos del encabezado\n(arquitectura, cuantización);
:Registrar la unidad completa\nsin copiar ni mover archivos;
stop
@enduml
```

## Retirar un modelo o backend

```plantuml
@startuml
start
:Resolver la unidad registrada;
if (¿está en uso por una instancia?) then (sí)
  :Rechazar; primero detener la instancia;
  stop
endif
if (¿archivos gestionados por Coquena?) then (sí)
  :Borrar todos sus archivos propios\ny retirar el registro;
  :Informar los bytes liberados;
else (no, externos)
  :Retirar sólo el registro;
  :Avisar que los archivos permanecen;
endif
stop
@enduml
```

La eliminación de un conjunto de shards no debe dejar una parte registrada como modelo
completo. Si otro registro comparte alguno de sus archivos, la regla de propiedad se
resolverá antes de implementar la eliminación gestionada.

## Clonar un preset

```plantuml
@startuml
start
:Usuario elige un preset origen\ny un modelo destino;
:Copiar los valores propios del preset\n(no los heredados);
:Pedir nombre nuevo;
if (¿nombre válido y libre?) then (no)
  :Rechazar;
  stop
endif
if (¿el preset tenía companions\nincompatibles con el modelo destino?) then (sí)
  :Quitarlos y avisar;
endif
:Guardar el preset\nbajo la configuración del modelo destino;
stop
@enduml
```


## Asociar un companion a un preset

```plantuml
@startuml
start
:Usuario asocia un companion a un preset\n(ubicación y --force opcionales);
if (¿el companion está disponible?) then (no)
  :Rechazar;
  stop
endif
:Leer metadatos GGUF\ndel modelo y del companion;
if (¿compatibilidad demostrada?) then (sí)
else (no o desconocida)
  if (¿--force?) then (no)
    :Rechazar explicando\nla evidencia o su ausencia;
    stop
  else (sí)
    :Avisar y registrar que la\nasociación fue forzada;
  endif
endif
:Guardar la asociación\ncon su ubicación (GPU / CPU);
stop
@enduml
```

Forzar permite **experimentar** con un companion de otra cuantización o de un
modelo derivado, sin declararlo compatible. La decisión y el diagnóstico quedan
visibles en el preset. La disponibilidad del archivo se comprueba aparte; `--force`
no convierte un archivo faltante en servible. Un mismo companion puede ser usado por
varios presets y su ubicación CPU/GPU se elige por asociación.
