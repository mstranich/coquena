# Secuencias del catálogo y las descargas

Contratos de interacción para implementar en C#. Los participantes representan
responsabilidades, no proyectos, interfaces ni servicios de red obligatorios.
Complementan [modelos y companions](modelos-y-companions.md),
[descargas](descargas.md) y la [guía técnica](implementacion-catalogo-modelos.md).
La estructura JSON acordada está en la guía técnica. Estos diagramas no eligen los
mecanismos de exclusión o recuperación ni cierran la gramática marcada como pendiente.

## Correspondencia entre reglas y secuencias

| Secuencia | Reglas aplicadas |
|---|---|
| 1. Alta externa | Intento de emparejamiento, `--try-match`, persistencia local; no inicia descarga ni seguimiento de externos |
| 2. Descarga inicial | Cuantización opcional, menú único, README a pedido, cancelar sin trabajo; inicio en primer plano, GUID y Ctrl+C conservador |
| 3. Update | Sólo metadatos y plan; sin menú de adquisición, descarga ni GUID de transferencia |
| 4. Upgrade | Plan ya seleccionado; no repite selección de cuantización; transferencia en primer plano, GUID y Ctrl+C conservador |
| 5. Backend | Selección propia de hardware/release; sin menú de cuantizaciones ni README de modelos; transferencia en primer plano y reanudable |
| 6. Pausa y reanudación | Procedimiento común de descarga; conserva GUID, parciales y selección; no repite búsqueda ni menú |
| 7. Integridad | Lectura local y resultado persistido; --set-baseline sólo establece base explícita en externos; sin red |

El menú de adquisición pertenece a la secuencia 2; no se propaga a operaciones que ya
tienen selección fijada. Las cajas de catálogo, operaciones e índice representan
persistencia local en disco, no almacenamiento de Hugging Face ni una base de datos
obligatoria. Operaciones activas/recuperables y catálogo instalado son datos distintos.
Al terminar y limpiar, la operación se elimina; sólo queda su resultado en el log.

## 1. Registrar un externo e intentar emparejarlo

Los archivos GGUF externos y el catálogo son dos almacenamientos locales distintos:
los primeros contienen los pesos; el segundo persiste nombre canónico, rutas y metadatos propios
de Coquena, sin copiar pesos. Sólo Hugging Face está en Internet.

El nombre es obligatorio; el intento remoto es opcional:
`model add <ruta.gguf> --name <usuario/repositorio:cuantización> [--try-match]`.

```plantuml
@startuml
actor Usuario as U
box "PC del usuario — local" #EEF4FA
participant "Coquena\ncomando model add" as C
collections "Archivos externos en disco\nGGUF y sus partes" as F
database "Catálogo local\ncatalogs/models.json" as I
end box
box "Internet" #FFF3DB
participant "Hugging Face\nbúsqueda y metadatos" as HF
end box
U -> C : agregar ruta y --name obligatorio\n[--try-match opcional]
C -> F : leer metadatos de división y localizar hermanos\ndel primer shard en la misma carpeta
F --> C : metadatos y rutas / error
alt archivo inaccesible o GGUF inválido
  C --> U : no registrado: indicar archivo y causa
else unidad identificada
  C -> I : persistir nombre canónico, rutas y propiedad externa\nsólo si nombre canónico libre
  I --> C : guardado / nombre canónico ocupado / error de escritura
  alt registro no guardado
    C --> U : no registrado: explicar colisión o fallo
  else registro guardado
    C --> U : registrado; completo o Incompleto con faltantes\nsin copiar; uso bloqueado si incompleto
    opt --try-match indicado
      C -> HF : consultar selección en repositorio declarado\ncon nombre y metadatos locales
    HF --> C : candidatos / sin coincidencias / error
    alt candidatos encontrados
      C --> U : menú: candidatos y razones / Cancelar empate\nno certifica bytes ni revisión
      U -> C : elegir candidato / cancelar emparejamiento
      alt candidato elegido
        C -> I : guardar equivalencia informativa seleccionada\nsin cambiar nombre ni habilitar seguimiento
        I --> C : guardado / fallo
        C --> U : emparejamiento guardado o diagnóstico\nregistro externo permanece
      else cancela
        C --> U : registrado localmente sin emparejar
      end
    else sin coincidencia suficiente
      C --> U : registrado sin emparejar; no se identificó equivalente
    else consulta fallida
      C --> U : registrado; no se pudo consultar Hugging Face
    end
    end
  end
end
note over C,I
  El intento remoto no habilita update/upgrade del externo.
  No descarga pesos ni exige hash completo.
end note
@enduml
```

Sin `--try-match`, no se consulta la red. El nombre declarado no prueba procedencia.
Un intento fallido o cancelar el menú no revierte el registro. La elección explícita
guarda equivalencia informativa, no certificación de procedencia ni revisión.
Configurar un preset y generar el INI son operaciones separadas de este diagrama.

Aplicar la agrupación de [shards](modelos-y-companions.md#modelos-divididos-en-shards).
El registro conserva todas las partes esperadas, incluso ausentes; informa Incompleto
y bloquea su uso. No presenta el primer shard como un modelo completo.

## 2. Descargar un modelo desde Hugging Face

Esta secuencia es la adquisición inicial, no `upgrade` ni el alta de un archivo externo.
Entrada: `usuario/repositorio[:cuantización]`, sin alias ni nombre adicional.
El menú determina la cuantización omitida y, con ella, el nombre canónico.

```plantuml
@startuml
actor Usuario as U
box "PC del usuario" #EEF4FA
participant "Coquena\nselección remota" as C
participant "Administrador de descargas" as D
collections "Preparación local\ndownloads/<guid>/files" as F
database "Catálogo local de Coquena" as I
end box
box "Internet" #FFF3DB
participant "Hugging Face" as HF
end box
U -> C : descargar usuario/repositorio[:cuantización]
C -> HF : resolver revisión y consultar inventario\nen un commit concreto
HF --> C : archivos y metadatos / error
C -> C : identificar variantes y conjuntos completos\nfiltrar cuantización si fue indicada
alt error de consulta o cuantización indicada inexistente
  C --> U : explicar; no crear descarga
else repositorio consultado sin GGUF seleccionables
  loop inspección hasta cancelar
    C --> U : sin candidatos: Mostrar README / Cancelar\nsin opción de descarga
    U -> C : elegir acción
    opt mostrar README
      C -> HF : consultar README del commit resuelto
      HF --> C : documento / ausente / error
      C --> U : mostrar documento o diagnóstico; volver al menú
    end
  end
  C --> U : volver al prompt; sin GUID ni operación
else candidatos disponibles
  loop menú hasta elegir descargar o cancelar
    alt cuantización especificada y única
      C --> U : Descargar selección / Mostrar README / Cancelar
    else cuantización omitida o ambigua
      C --> U : Descargar variante A / Descargar variante B / ...\nMostrar README / Cancelar
    end
    U -> C : elegir acción
    opt mostrar README
      C -> HF : consultar README del commit resuelto
      HF --> C : documento / ausente / error
      C --> U : mostrar documento o aviso; volver al menú
    end
  end
  break usuario eligió cancelar antes de descargar
    C --> U : volver al prompt; sin GUID ni parciales
  end
  C -> C : seleccionar unidad elegida\ne iniciar sin segunda confirmación
  C -> I : comprobar disponibilidad del nombre canónico
  I --> C : libre / externo / otro registro / error
  opt nombre ocupado por externo
    C --> U : ¿pasar este registro a gestionado?\nsin tocar archivos externos; conservar ID y presets
    U -> C : aceptar / cancelar
    C -> C : fijar transición explícita y registro de partida\no terminar sin cambios
  end
  alt otra colisión, transición cancelada o error local
    C --> U : explicar; no iniciar descarga
  else nombre libre o transición aceptada
    C -> D : crear operación con selección y commit fijados\nsin companions
    D --> U : GUID de operación\nla terminal queda ocupada
    loop cada archivo de la unidad
      D -> HF : descargar archivo del commit fijado
      HF --> D : bytes / error
      break Ctrl+C durante transferencia (termina la invocación)
        U -> D : solicitar interrupción conservadora
        D -> D : detener escritores y persistir pausa\nconservar parciales y selección
        D --> U : GUID y comando resume; devolver prompt\nsin publicar ni borrar parciales
      end
      D -> F : escribir parcial y verificar contenido
      D --> U : progreso individual y del conjunto
    end
    alt algún archivo falló o conjunto incompleto
      D -> D : detener escritores; guardar fallo y parciales
      D --> U : no instalada; GUID y comando resume\ndevolver prompt
    else conjunto completo y verificado
      D -> I : coordinar destino y catálogo\ncomprobar nombre libre o externo de partida vigente\nen transición conservar ID y referencias
      I --> D : instalación registrada / conflicto / error
      opt publicación y limpieza completadas
        D -> D : registrar resultado en log\ny eliminar downloads/<guid> incluido JSON
      end
      D --> U : modelo disponible o fallo de publicación
    end
  end
end
note over C,D
  No buscar, descargar ni encolar companions
  como efecto de descargar el modelo principal.
end note
@enduml
```

La cuantización identifica candidatos, no necesariamente un archivo único. Las opciones
del menú deben identificar las variantes inequívocamente y declarar que elegirlas inicia
una descarga. Mostrar README no modifica la selección y vuelve al menú. Sin candidatos
no hay opción de descarga: sólo mostrar README o cancelar, sin crear operación.
Aplicar la agrupación de shards documentada; conjuntos remotos incompletos no son descargables
como instalaciones completas.
La publicación usa el protocolo recuperable de la guía técnica: una
colisión tardía no autoriza a sobrescribir otro registro.

### Descarga del companion: otra invocación

El usuario ejecutará `model companion download usuario/repositorio#tipo[:cuantización]`
por separado, cuando lo necesite, usando el mismo flujo de selección y descarga
de la secuencia 2, limitado al tipo auxiliar indicado.
Ese comando creará una unidad registrada independiente, reutilizable mediante referencias
de configuración. No requerirá repetir la descarga del principal ni lo vinculará a un
preset. No promete compatibilidad funcional; las secuencias de update, upgrade e
integridad se aplican igualmente a esta unidad auxiliar.

## 3. Consultar novedades sin descargar pesos

```plantuml
@startuml
actor Usuario as U
participant CLI
participant "Caso de uso Update" as UP
database "Índice central" as I
participant "Adaptador Hugging Face" as HF
participant "Comparador puro" as CMP
U -> CLI : model update nombre
CLI -> UP : consultar(nombre)
UP -> I : leer instalación y origen seguido
I --> UP : registro
alt externo
  UP --> CLI : seguimiento no aplica
else gestionado
  UP -> HF : resolver referencia solicitada
  HF --> UP : commit objetivo / error
  alt no se pudo resolver
    UP -> I : registrar intento fallido\nsin borrar último éxito
  else commit resuelto
    UP -> HF : consultar inventario en commit\ncompletar paginación necesaria
    HF --> UP : metadatos / error
    alt consulta incompleta o fallida
      UP -> I : registrar fallo, no inferir ausencias
    else inventario suficiente
      UP -> CMP : comparar instalación y correspondencias\ncontra inventario fijado
      CMP --> UP : sin cambios / reubicación /\nplan candidato / requiere intervención
      opt sucesor ambiguo con candidatos
        UP --> CLI : candidatos y diferencias para menú
        CLI --> U : elegir candidato / Cancelar
        U -> CLI : elección / cancelación
        CLI -> UP : resultado del menú
        alt candidato elegido explícitamente
          UP -> CMP : resolver correspondencia elegida\ny plan en commit fijado
          CMP --> UP : destino y plan / diagnóstico
        else cancela
          UP -> UP : conservar seguimiento sin resolver\ny archivos instalados
        end
      end
      UP -> I : guardar observación y candidato\nligados a instalación de partida
    end
  end
  I --> UP : guardado / error
  UP --> CLI : resultado de consulta y persistencia
end
CLI --> U : diferencias, fecha y diagnóstico
note over UP,HF
  No descargar pesos, recorrer todo el historial
  ni cambiar instalación activa o commit de descarga.
end note
@enduml
```

El comparador aplica las reglas de correspondencia documentadas, no heurísticas nuevas.
Una reubicación exacta puede cambiar el seguimiento, no la ruta física instalada.
El movimiento físico y cambio de nombre canónico se aplican en upgrade, no en update.
Esto incluye espacios de nombres elegidos mediante candidatos: upgrade renombra
carpetas y archivos gestionados, conservando IDs y referencias. Sin origen ni candidatos
no hay plan aplicable; no se bloquea por ello la disponibilidad local.
Sin nombre, repetir la consulta por cada gestionado, incluidos companions; su subcomando
limita el conjunto a companions. Un fallo no impide consultar las demás unidades.
Si no se guarda el candidato, no se presenta como un plan durable listo para aplicar.

## 4. Preparar y publicar una actualización de modelo

```plantuml
@startuml
actor Usuario as U
participant CLI
participant "Caso de uso Upgrade" as UG
database "Índice / operaciones" as I
participant "Administrador de descargas" as D
participant "Acceso local y verificador" as F
participant "Hugging Face" as HF
U -> CLI : model upgrade nombre
CLI -> UG : aplicar actualización detectada
UG -> I : leer plan, instalación actual y propiedad
I --> UG : datos
alt externo, candidato ambiguo o plan obsoleto
  UG --> CLI : rechazar aplicación; explicar acción necesaria
else plan aplicable
  UG -> I : persistir operación y conjunto objetivo fijado
  I --> UG : operación creada / error
  opt operación persistida correctamente
    UG --> CLI : GUID de operación
    CLI --> U : GUID; descarga en primer plano\nla terminal permanece ocupada
    loop cada archivo del conjunto objetivo
      alt contenido local reutilizable
        UG -> F : comprobar evidencia local antes de reutilizar
        F --> UG : comprobado / error
      else archivo nuevo o modificado
        UG -> D : transferir archivo del commit fijado
        D -> HF : obtener bytes en preparación
        HF --> D : flujo / fallo
        break Ctrl+C durante transferencia (termina la invocación)
          U -> CLI : interrumpir ejecución
          CLI -> D : pausar operación
          D -> D : detener escritores y conservar parciales
          D -> I : persistir pausa y checkpoints
          I --> D : confirmado / fallo explícito
          D --> CLI : GUID y estado de pausa
          CLI --> U : comando resume y diagnóstico; devolver prompt\ninstalación anterior conservada
        end
        D -> I : persistir progreso y estado
        D --> CLI : progreso por archivo
        D --> UG : transferencia terminada / fallo
      end
    end
    UG -> F : verificar conjunto preparado completo
    F --> UG : íntegro y legible / fallo
    alt algún archivo falló o conjunto inválido
      UG -> I : registrar fallo recuperable
      note over UG,I
        La instalación anterior sigue activa.
      end note
    else conjunto verificado
      UG -> F : aplicar archivos y renombres al destino remoto reflejado\nsin sobrescribir archivos ajenos
      F --> UG : preparado para activar / fallo o inconsistencia
      opt destino preparado sin errores
      UG -> I : activar conjunto si instalación de partida\nsigue vigente; escritura protegida
      I --> UG : activado / conflicto / fallo
      opt activación confirmada
        UG -> F : limpiar archivos anteriores propios\ncuando sea seguro
        F --> UG : limpieza terminada / pendiente
        UG -> I : registrar resultado de limpieza
        opt limpieza completada
          UG -> UG : registrar resultado en log\ny eliminar carpeta de operación incluido JSON
        end
      end
      end
    end
  end
  UG --> CLI : resultado durable y diagnóstico
end
CLI --> U : resultado de instalación, no sólo porcentaje
@enduml
```

El orden del bucle es ilustrativo, no prescribe paralelismo. Un fallo impide publicar;
si procede continuar otras transferencias es una política pendiente. La descarga
inicial usa selección fijada sin instalación anterior; nunca publica una unidad parcial.
El mecanismo de activación y limpieza debe resolver concurrencia, archivos compartidos
y modelos en uso antes de su implementación definitiva. Si el movimiento físico falla,
no activar el catálogo; diagnosticar cualquier inconsistencia sin reparación destructiva.
Sin nombre, aplicar secuencialmente planes resueltos de las unidades seleccionadas;
no hay transacción global ni cambio silencioso de revisión.

## 5. Instalar un backend desde una selección concreta

```plantuml
@startuml
actor Usuario as U
participant "CLI / caso de uso" as C
participant "Catálogo de backends" as B
participant "Contexto de hardware" as H
participant "Administrador de descargas" as D
participant "Verificador e instalador" as V
database "Registro de backends" as I
U -> C : backend install filtros
C -> H : obtener plataforma y evidencia de aceleración
H --> C : contexto
C -> B : resolver origen oficial, release, familia\ny artefactos con ese contexto
B --> C : selección y alternativas / diagnóstico
alt selección inválida
  C --> U : explicar; no crear descarga
else selección válida
  C --> U : informar elección y alternativas
  C -> D : persistir una operación para el conjunto completo
  D --> U : GUID de operación\nla terminal permanece ocupada
  loop artefactos y dependencias seleccionados
    C -> D : transferir artefacto de la selección fijada\nbajo el mismo GUID
    D --> U : progreso por archivo y conjunto
    break Ctrl+C durante transferencia (termina la invocación)
      U -> D : solicitar pausa conservando parciales
      D -> D : detener escritores y persistir pausa
      D --> U : GUID y comando resume; devolver prompt\nsin instalar ni borrar parciales
    end
    D --> C : resultado de transferencia
    break transferencia fallida (termina la instalación)
      C --> U : diagnóstico recuperable y GUID\nno extraer ni instalar
    end
  end
  C -> V : verificar conjunto y extraer en preparación
  V -> V : límites y rutas seguras;\nllama-server --version
  alt verificación o sondeo fallido
    V --> C : fallo; no instalar
  else validación completa
    V -> I : publicar instalación y registro
    I --> V : confirmado / error
    V --> C : resultado
  end
  opt publicación y limpieza completadas
    C -> D : registrar resultado en log\ny eliminar carpeta de operación incluido JSON
  end
  C --> U : instalado o diagnóstico
end
@enduml
```

La selección sigue las convenciones de comandos: release y versión de bibliotecas son
independientes; explícito y automático tienen reglas distintas de compatibilidad.
Transferencias fallidas impiden instalar; el sondeo no certifica toda inferencia futura.

## 6. Pausar, recuperar y reanudar una transferencia

```plantuml
@startuml
actor Usuario as U
participant "Control de descargas" as D
participant "Escritor de transferencia" as W
database "Operaciones persistidas" as I
participant "Sistema de archivos" as F
participant "Origen HTTP" as H
participant "Publicación según recurso" as P
U -> D : Ctrl+C durante descarga en primer plano\no pausa explícita del GUID
D -> W : solicitar detención
W -> F : terminar escritura y cerrar parcial
F --> W : resultado
W --> D : escritor detenido / error
D -> I : persistir checkpoint y pausa coherentes
I --> D : confirmado / fallo
D --> U : pausa confirmada, GUID y\ncoquena download resume <guid>\no fallo explícito; devolver prompt
... cierre y nueva ejecución ...
D -> I : cargar operaciones no terminadas
I --> D : registros y ejecutores
D -> D : distinguir ejecutor vivo de interrupción
opt trabajo realmente interrumpido
  D -> F : contrastar parcial y checkpoint
  F --> D : tamaño y accesibilidad
  D -> I : registrar pausado o fallo recuperable
end
note over D,H
  La recuperación no inicia red automáticamente.
end note
U -> D : coquena download resume <guid>\nocupa nuevamente la terminal
note over D,I
  Reanudar carga la selección del mismo trabajo.
  No repite búsqueda, cuantización ni menú README.
end note
D -> I : adquirir control según mecanismo acordado\ny cargar selección persistida
I --> D : control y selección / conflicto
opt control obtenido y parcial coherente
  D -> H : Range desde offset validado\nvalidador cuando esté disponible
  H --> D : respuesta y encabezados
  alt rango e identidad válidos
    D -> W : continuar parcial
    W -> F : escribir resto; validar según recurso\nmodelos/companions: SHA-256 de prefijo y resto\nbackends: sin SHA-256
    W --> D : archivo verificado / fallo
    alt archivo verificado
      D -> I : persistir archivo completo y progreso del conjunto
      loop otros archivos pendientes de la selección fijada
        D -> D : transferir y verificar; mismos controles
      end
      alt conjunto completo y verificado
        D -> P : continuar instalación o upgrade del mismo trabajo
        P -> I : publicar según protocolo recuperable\nmodelo o backend
        I --> P : confirmado / conflicto / error
        P --> D : instalado / no publicado / limpieza pendiente
      else conjunto con archivos pendientes o fallidos
        D -> I : conservar operación recuperable; no publicar
      end
    else archivo inválido o ilegible
      D -> I : registrar fallo; no publicar
    end
  else no es seguro continuar
    D -> I : registrar diagnóstico\nsin añadir bytes incompatibles
    opt Range no soportado pero selección original disponible
      D --> U : ¿reiniciar sólo este archivo desde cero?
      U -> D : aceptar / rechazar
      alt acepta explícitamente
        D -> F : descartar sólo parcial de este archivo
        D -> H : descargar desde cero la misma selección
        H --> D : bytes / fallo
        D -> D : verificar y continuar conjunto\nsin repetir archivos ya verificados
        opt conjunto completo y verificado
          D -> P : validar y publicar la misma operación
          P --> D : instalado / fallo / limpieza pendiente
        end
      else rechaza o no hay respuesta
        D -> I : conservar parcial y operación recuperable
      end
    end
  end
end
opt publicación y limpieza completadas
  D -> D : registrar resultado en log\ny eliminar carpeta de operación incluido JSON
end
D --> U : resultado y progreso
@enduml
```

Si Range no permite continuar, conservar el parcial y preguntar antes de reiniciar
ese archivo desde cero, con el mismo GUID y selección. No repetir archivos completos
y verificados. Rechazar o no poder preguntar conserva el trabajo sin reinicio automático.
El GUID identifica la operación tanto de modelos como de backends. Ctrl+C conserva
los parciales: no equivale al comando `cancel`. La descarga activa y su reanudación
permanecen en primer plano; no devuelven el prompt mientras siguen transfiriendo.
Cancelar sigue la misma detención y espera del escritor, pero luego elimina únicamente
sus parciales y registra cualquier limpieza pendiente; no toca instalaciones publicadas.
La continuación ejecuta el protocolo de modelo/upgrade (secuencias 2 y 4) o backend
(secuencia 5), sin una segunda selección. Recuperar los bytes no basta para completar
una operación: resume también debe terminar su validación y publicación.

## 7. Verificación local de integridad

```plantuml
@startuml
actor Usuario as U
participant "Verificación local" as V
database "Índice central" as I
participant "Lector de archivos" as F
U -> V : model verify nombre [--set-baseline]\no model companion verify nombre [--set-baseline]
V -> I : leer archivos y referencias de integridad
I --> V : datos
alt --set-baseline y registro gestionado
  V --> U : rechazar; no modificar referencia de descarga
else --set-baseline y registro externo
  loop cada archivo esperado
    V -> F : leer completo y calcular SHA-256\ncomprobar errores y cambios durante lectura
    F --> V : digest y fecha / fallo
  end
  alt conjunto leído sin fallos ni cambios detectados
    V -> I : establecer base explícita y fecha\ncreación o sustitución, no verificación
    I --> V : confirmado / error
    V --> U : base registrada o fallo\nno certifica origen ni corrección original
  else lectura incompleta o inestable
    V --> U : no establecer base completa; explicar fallo
  end
else verify normal
loop archivo de la unidad
  alt no hay referencia de integridad
    V -> V : resultado: no verificable sin establecer base
  else existe referencia
    V -> F : leer completo con memoria acotada\ny calcular SHA-256 de los bytes
    F --> V : digest / error de lectura
    V -> V : comparar; distinguir discrepancia de error
  end
end
V -> I : guardar resultados y fecha\nsin reemplazar referencias de integridad
I --> V : confirmado / error
V --> U : resultado por archivo y del conjunto
end
note over V,F
  Sin red. No establece revisión remota.
  No repara ni cambia los pesos.
end note
@enduml
```

## Validación de las secuencias

Traducir ramas alternativas a pruebas: colisión de nombre, consulta remota incompleta,
plan obsoleto, fallo al persistir, shard fallido, extracción inválida, ejecutor aún vivo,
Range incompatible y discrepancia de integridad. Los diagramas no prueban código ni
resuelven por sí solos la atomicidad del índice y el sistema de archivos.
