# Secuencias del proyecto y las descargas

## Correspondencia entre reglas y secuencias

Estas secuencias ilustran contratos de [descargas](descargas.md),
[actualización](actualizacion-y-limpieza.md) y [servidores](servidores.md).
No fijan clases ni una API común para todos los forks.

## 1. Registrar un externo e intentar emparejarlo

Seleccionar proyecto y registrar rutas/metadatos locales antes del intento optativo
--try-match. Si el usuario copió archivos al proyecto, registrar esa copia, no la
ubicación de la herramienta de origen. No transferir pesos para completar el alta.
Un fallo o cancelación del matching conserva el alta y la propiedad externa.
Elegir candidato registra una equivalencia informativa, sin certificar bytes.

## 2. Descargar un modelo desde Hugging Face

Resolver commit, consultar inventario y candidatos con lecturas acotadas;
ofrecer README/selección/cancelación antes de crear GUID.
Después transferir selección completa, verificar y publicar inventario.

### Descarga del companion: otra invocación

La descarga explícita del principal no añade companions implícitos.
Cuando se prepara YAML, sus companions declarados sí forman parte de los recursos
necesarios y se coordinan junto al resto del conjunto.

## 3. Consultar novedades sin descargar pesos

```plantuml
@startuml
actor Usuario
participant CLI
participant Resolver
participant Origen
database Proyecto
Usuario -> CLI : update / combinación y filtros
CLI -> Proyecto : leer intención y resolución
CLI -> Resolver : comparar selección conocida
Resolver -> Origen : metadatos de revisiones/archivos
Origen --> Resolver : candidatos o diagnóstico
Resolver --> CLI : diferencias y selección fijada
CLI -> Proyecto : guardar plan local
CLI --> Usuario : versiones actuales y propuestas
@enduml
```

## 4. Preparar y publicar una actualización de modelo

```plantuml
@startuml
actor Usuario
participant CLI
database Proyecto
participant Motor
participant Servidor
Usuario -> CLI : upgrade / selector opcional
CLI -> Proyecto : leer plan y validar vigencia
loop todas las unidades seleccionadas
CLI -> Motor : preparar revisión fijada
Motor -> Motor : transferir y verificar conjunto
Motor --> CLI : preparada o error
end
alt todas preparadas
CLI -> Proyecto : publicar resoluciones del conjunto
CLI --> Usuario : disponibles para próxima aplicación
else fallo
CLI -> Proyecto : conservar resoluciones anteriores
CLI --> Usuario : diagnóstico y recuperación posible
end
note over Servidor
La instancia actual continúa.
Sus recursos no se sobrescriben.
end note
@enduml
```

## 5. Instalar un backend desde una selección concreta

Preparar artefactos y validar antes de publicar, conservando archivos activos.
No sustituir variante al diagnosticar incompatibilidad de hardware.

### Aplicar el candidato guardado de backend

Consume el plan fijado, como modelos. No modifica intención YAML.
Cambiar el ejecutable activo requiere reinicio explícito.

### Consultar novedades de backends

Update compara el origen/variante de la combinación seleccionada;
no infiere que CUDA y Vulkan sean reemplazos intercambiables.

## 6. Pausar, recuperar y reanudar una transferencia

Persistir selección/checkpoints; interrumpir escritores; conservar parciales.
Al reanudar, verificar ejecutor, tamaño y continuidad HTTP antes de anexar bytes.
No mezclar respuestas 200 con un parcial ni publicar sin integridad completa.

## 7. Verificación local de integridad

```plantuml
@startuml
actor Usuario
participant Verify
database Proyecto
participant Archivos
Usuario -> Verify : verificar combinación
Verify -> Proyecto : resolución y referencias
Verify -> Verify : validar YAML/esquema
loop backend, modelos y companions seleccionados
Verify -> Archivos : leer/hash completo
Archivos --> Verify : resultado por archivo
Verify -> Proyecto : registrar resultado por recurso
end
Verify --> Usuario : diagnóstico, sin reparación
note over Proyecto
Fallo conocido bloquea próximos arranques
que seleccionen ese recurso.
No se detiene la instancia activa.
end note
@enduml
```

## Validación de las secuencias

La evidencia debe cubrir interrupciones entre fases, escrituras concurrentes y
fallos de verificación sin pérdida de la revisión anterior. Los diagramas válidos
no acreditan por sí mismos un protocolo de publicación recuperable.

## Reiniciar un servidor

```plantuml
@startuml
actor Usuario
participant Coquena
database Proyecto
participant Backend
Usuario -> Coquena : reinicio explícito
Coquena -> Proyecto : validar intención y fallos conocidos
Coquena -> Coquena : preparar/verificar recursos nuevos
alt preparación falla
Coquena --> Usuario : informar; conservar servidor actual
else preparada
Coquena -> Backend : detener anterior
Coquena -> Backend : iniciar candidata
Coquena -> Backend : healthcheck del router
alt sana
Coquena -> Proyecto : composición activa y última buena elegible
else falla recuperable en daemon
Coquena -> Proyecto : obtener última buena local
Coquena -> Backend : iniciar anterior y comprobar salud
Coquena --> Usuario : warning o fallo fatal, sin bucle
end
end
@enduml
```

## Crear o reasignar el puerto de un servidor

Guardar puerto modifica intención. Comprobar colisión al iniciar, sin asignación
automática alternativa ni prohibición de repetirlo en proyectos detenidos.

## Aplicar presets y observar carga

Publicar definición en el router compatible y cargar memoria son dos resultados.
El adaptador debe observar eventos antes de disparar cuando sea posible, confirmar
estado final y reconciliar al reconectar. No mantener polling global desde Coquena
después de terminar el comando.

No asumir transacción nativa de apply ni confundir HTTP exitoso con modelo cargado.
La recuperación ante fallo individual tardío se concreta con evidencia del build.

## Publicar y retirar una receta consumidora

Up y down modifican el mismo INI operativo del servicio target gestionado por
Coquena: up agrega o actualiza los presets del consumidor; down los remueve.
Después ambos solicitan al servidor en funcionamiento refrescar sus fuentes de
presets. Target es default salvo selección explícita. La retirada comprueba además
el destino registrado de la publicación, para no actuar sobre otro servicio si
cambió el alias o se editó la receta.

```plantuml
@startuml
actor Usuario
participant Coquena
database Consumidor
database Servicio
database "INI operativo del target" as INI
participant Router
Usuario -> Coquena : up / combinación seleccionada
Coquena -> Consumidor : validar y preparar recursos propios
Coquena -> Servicio : resolver target (default salvo selección explícita) e identidad
alt destino detenido
  Coquena -> Usuario : pedir inicio o exigir --start-service sin interacción
  opt inicio autorizado
    Coquena -> Router : iniciar y comprobar salud
  end
end
alt servicio disponible
  Coquena -> Servicio : reservar revisión y proteger referencias
  Coquena -> INI : agregar / actualizar presets propios\nconservar contribuciones ajenas y publicar INI
  Coquena -> Router : refrescar fuentes de presets (reload una vez)
  Coquena -> Router : observar catálogo resultante
  Coquena -> Servicio : registrar confirmado o incierto
  opt warmup explícito
    Coquena -> Router : solicitar carga de entradas nuevas o modificadas
  end
else detenido o no disponible
  Coquena --> Usuario : no acreditar publicación
end
Usuario -> Coquena : down
Coquena -> Servicio : resolver target (default salvo selección explícita)\ncomprobar identidad y destino publicado registrado
Coquena -> Servicio : reservar revisión y conservar referencias hasta confirmar
Coquena -> INI : remover presets propios registrados\nconservar contribuciones ajenas y publicar INI
alt servicio activo y accesible
  Coquena -> Router : refrescar fuentes de presets (reload una vez)
  Coquena -> Router : observar catálogo y confirmar retirada
  Coquena -> Servicio : registrar confirmado o incierto
else servicio detenido o inaccesible
  Coquena --> Usuario : informar INI actualizado y estado de aplicación\nsin acreditar refresco del servidor
end
Coquena --> Usuario : resultado / servicio y entradas ajenas conservados
@enduml
```

La reserva de publicación evita superponer escritores pero no retiene un bloqueo
de archivo durante HTTP. Si reload queda incierto, se conserva operación y recursos
hasta reconciliar. El protocolo se concreta en [persistencia](persistencia-proyectos.md).
