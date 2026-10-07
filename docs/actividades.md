# Actividades complementarias

Los flujos de modelos y descargas se describen en [secuencias.md](secuencias.md).
Aquí sólo se conservan actividades complementarias; los pendientes de ejecución están en servidores.md.

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
:Crear operación única y descargar en preparación;
if (¿transferencia completa?) then (no)
  :Detener escritores, conservar trabajo\ny mostrar GUID + resume; devolver prompt;
  stop
endif
:Verificar artefactos y extraer en preparación;
:Sondear llama-server --version;
if (¿responde?) then (sí)
  :Asignar ID único y registrar\nel backend instalado;
  :Conservar intención latest o pin;\nsi es la primera instalación propia y no hay\nselección previa, asignar backend default;
  :Registrar resultado en log y limpiar operación\neliminando su JSON;
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
:Usuario indica un llama-server externo,\nnombre obligatorio y familia declarada;
if (¿nombre válido y libre?) then (no)
  :Rechazar sin registrar;
  stop
endif
if (¿ruta existente y ejecutable?) then (no)
  :Rechazar sin registrar;
  stop
endif
:Ejecutar llama-server --version;
if (¿termina correctamente y\nla versión es reconocible?) then (no)
  :Rechazar con diagnóstico;
  stop
endif
:Asignar ID único y registrar nombre, ruta,\nfamilia declarada, versión sondeada\ny origen externo sin mover archivos;
stop
@enduml
```

El sondeo valida el ejecutable, no demuestra compatibilidad con una GPU, no verifica
la familia declarada ni hace una prueba de inferencia. Coquena no infiere la familia
del nombre de la carpeta. Al retirarlo sólo se borra su registro.

## Iniciar una instancia

Reglas en [servidores.md](servidores.md); no se descargan actualizaciones al iniciar.

```plantuml
@startuml
start
:server [servidor] start [-d];
:Resolver servidor indicado\no predeterminado;
if (¿ya tiene instancia activa?) then (sí)
  :Informar que ya está ejecutándose;
  stop
endif
:Resolver backend propio o default\ny build actual instalado;
if (¿backend ausente?) then (sí)
  :Rechazar sin seleccionar sustituto;
  stop
endif
:Resolver presets explícitos\ny sus dependencias locales;
if (¿vacío o algún preset no servible?) then (sí)
  :Rechazar todo el inicio\ncon diagnóstico;
  stop
endif
:Resolver defaults y valores propios\ndel servidor (host, port, metrics, otros);
if (¿puerto ocupado?) then (sí)
  :Rechazar sin reasignar puerto;
  stop
endif
:Generar INI por instancia\ncon herencia de modelos/presets;
:Asignar ID, lanzar backend concreto\ny registrar PID, escucha y log;
if (¿-d?) then (sí)
  :Informar ID y log;\ndevolver prompt;
else (no)
  :Mostrar salida;
  :Ctrl+C detiene esta instancia\ny devuelve prompt;
endif
stop
@enduml
```

El INI generado vive en el home de Coquena, no en temporales del sistema.
Cada instancia tiene el suyo; se elimina al detenerla. Los tiempos de salud,
verificación del proceso y terminación requieren diseño técnico. Las reglas funcionales
de recuperación están acordadas en servidores.md.

## Detener y consultar instancias

```plantuml
@startuml
actor Usuario
participant Coquena
participant "Instancia del backend" as Backend
Usuario -> Coquena : server stop <id-instancia>
Coquena -> Coquena : Resolver ejecución registrada
Coquena -> Backend : Detener esa instancia
Coquena -> Coquena : Conservar definición del servidor
Coquena --> Usuario : Informar resultado
@enduml
```

```plantuml
@startuml
actor Usuario
participant Coquena
participant "Instancias activas" as Instancias
Usuario -> Coquena : server ps
Coquena -> Coquena : Consultar registros de instancias
loop Por instancia y modelo
  Coquena -> Instancias : Consultar estado y métricas
  Instancias --> Coquena : Resultado o diagnóstico
end
Coquena --> Usuario : Instantánea con IDs y métricas disponibles
@enduml
```
## Clonar un preset

```plantuml
@startuml
start
:Usuario indica preset origen,\nnombre nuevo y modelo destino (opcional);
  :Copiar los valores propios del preset\n(no los heredados);
note right
  Incluye referencias propias a companions,
  no copia sus archivos ni valores heredados.
  El clon es independiente del preset origen.
end note
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
:config set <param> --ref <nombre>\n[ámbito];
:Resolver la referencia sin\nconfundirla con una ruta literal;
if (¿identifica un único registro?) then (no)
  :Rechazar con diagnóstico;
  stop
endif
:Guardar referencia por ID estable\nmostrando nombre canónico, no ruta resuelta;
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
referencia por nombre canónico de un valor literal; no se infiere por el contenido del texto.
