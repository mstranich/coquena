# Actividades complementarias

## Selección e instalación de backend

Seleccionar origen, versión y variante explícitos; resolver artefactos;
preparar/verificar sin sustituir recursos en uso. Diagnosticar incompatibilidad
sin cambiar la receta ni crear un bloqueo de integridad.

## Registrar un backend externo

Conservar rutas y propiedad ajenas. Identificar capacidades sin exigir parámetros
del oficial a todo ejecutable. Registrar no mueve ni adopta archivos.

## Iniciar una instancia

```plantuml
@startuml
start
:Resolver servicio, ámbito y combinación ordenada;
:Validar sintaxis, esquema y referencias;
if (¿Instancia ya activa?) then (sí)
  :Informar que ya está en ejecución;
  stop
endif
if (¿Fallo conocido en recurso seleccionado?) then (sí)
  :Informar integridad fatal y alerta configurada;
  stop
endif
:Preparar recursos requeridos sin renovar latest estable;
if (¿Candidata preparada y compatible?) then (sí)
  :Iniciar y comprobar salud del router;
  if (¿Salud válida?) then (sí)
    :Registrar composición y última buena elegible;
    stop
  endif
endif
if (¿Modo daemon?) then (sí)
  if (¿Última buena local disponible?) then (sí)
    :Preparar sus recursos exactos si faltan;
    :Iniciar y comprobar salud;
    if (¿Salud válida?) then (sí)
      :Warning / continuar con última buena;
      stop
    endif
  endif
  :Informar fallo y alerta opcional / terminar sin bucle;
else (no)
  :Ofrecer reintentar o recuperar según disponibilidad;
endif
stop
@enduml
```

Integridad fatal no se elude por recuperación. Si hay proceso activo, verify
no lo detiene. El flujo de reinicio prepara antes de detenerlo.
No cargar cada modelo como condición para aceptar salud del router.

## Detener y consultar instancias

Service stop solicita cierre y conserva definición/recursos. Down retira sólo los presets del consumidor sin detener el servicio. Ps consulta identidad de
proceso y API y termina. Watch/TUI/Web observan mientras permanecen abiertos;
cerrarlos no detiene el backend. Reconectar observadores no reinicia servidores.

## Clonar un preset

Copiar valores/vínculos, dar identidad/nombre independientes, validar resultado.
No copiar archivos ni conservar herencia viva respecto del original.

## Referenciar un companion desde la configuración

Bind selecciona recurso por identidad y argumento destino.
Comprobar referencia y reglas del schema si existe; no certificar compatibilidad
de inferencia por el nombre del companion.

## Guardar y aplicar desde una interfaz

```plantuml
@startuml
start
:Recuperar borrador local ligado al YAML;
:Editar en memoria y mantener temporal local;
if (¿Guardar definitivo?) then (sí)
  :Comparar versión de origen con disco;
  if (¿Disco cambió?) then (sí)
    :Ofrecer descartar o sobrescribir explícitamente;
  endif
  :Validar y guardar original o nuevo override;
endif
if (¿Aplicar explícitamente?) then (sí)
  :Preparar recursos y determinar capacidades;
  if (¿Cambió backend/opciones del proceso?) then (sí)
    :Reinicio explícito;
  else (no)
    :Aplicar presets sobre router compatible;
  endif
endif
stop
@enduml
```

La aplicación experimental puede preceder al guardado. Por defecto no se convierte
en última buena de recuperación; esa elegibilidad requiere opción del proyecto.
Guardar un override lo selecciona para acciones siguientes, sin aplicarlo por sí solo.
