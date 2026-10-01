# Diagramas de actividades

## Instalar una versión de backend

```plantuml
@startuml
start
:Usuario pide instalar\n(familia y versión opcionales);
if (¿indicó familia?) then (no)
  :Inspeccionar hardware;
  :Elegir la mejor familia compatible\n(cuda13 > cuda12 > vulkan > cpu);
else (sí)
endif
if (¿indicó versión?) then (no)
  :Tomar el último release\nde la distribución;
else (sí)
endif
:Buscar el binario para\nSO + arquitectura + familia;
if (¿existe?) then (no)
  :Informar qué familias\nsí están publicadas;
  stop
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
  :Registrar la versión instalada;
else (no)
  :Descartar la instalación;
  :Informar el error;
endif
stop
@enduml
```

## Iniciar una instancia

```plantuml
@startuml
start
:Usuario pide iniciar un servidor\n(backend, puerto y modo opcionales);
if (¿indicó familia?) then (no)
  if (¿el servidor fija familia?) then (sí)
    :Usar esa familia;
  else (no)
    :Inspeccionar hardware;
    :Elegir la familia instalada\nmás adecuada;
  endif
else (sí)
endif
if (¿indicó versión?) then (no)
  :Tomar la última versión instalada\nde esa familia;
else (sí)
endif
if (¿la versión está instalada?) then (no)
  :Rechazar y sugerir\nbackend install;
  stop
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
