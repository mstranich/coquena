# Modelo de datos

Relaciones conceptuales, sin fijar clases ni layout. Las reglas pertenecen a
[proyectos](definiciones-declarativas.md), [servidores](servidores.md),
[modelos](modelos-y-companions.md) y [parámetros](catalogo-de-parametros.md).

```plantuml
@startuml
hide empty methods
class Home {
  registroDeRutas
  preferenciasDeObservadores
  aliasDefault
}
class Proyecto {
  carpeta
}
class RegistroReceta {
  recipeId
  referenciaDeArchivo
  nombreRegistrado
  entidadesCreadas
}
class Combinacion {
  archivosOrdenados
  intencionEfectiva
}
class Lock {
  resolucionesConcretas
}
class Servicio {
  id
  nombre
  ambito
}
class Publicacion {
  consumerId
  nombreCompleto
  estadoObservado
}
class Backend
class Preset {
  nombreRouter
  argumentos
}
class Recurso {
  identidadLogica
  origen
}
class Instalacion {
  revisionFisica
  propiedad
  origenExplicitoOAutomatico
  integridadObservada
}
class Instancia {
  identidadProceso
  endpoint
  composicionActiva
}
class UltimaBuena {
  saludComprobada
  elegibilidadLocal
}
class Borrador {
  sesion
  archivoOrigen
  versionOrigen
}
class Schema {
  tiposYValidadores
}
Home --> "0..*" Proyecto : registra ruta
Proyecto *-- "0..*" RegistroReceta
Home --> RegistroReceta : índice de localización
RegistroReceta --> Proyecto : localiza receta
RegistroReceta --> "0..*" Servicio : conserva identidad creada
RegistroReceta --> "0..*" Publicacion : conserva nombres
Home o-- "0..*" Servicio : global importado
Proyecto *-- "0..1" Servicio : propio
Servicio --> "1" Backend : efectivo
Servicio *-- "0..*" Publicacion
Publicacion --> Preset : snapshot
Proyecto --> "0..*" Publicacion : consumidor
Servicio *-- "0..1" Instancia
Proyecto *-- "1..*" Combinacion
Combinacion --> "0..1" Lock
Combinacion --> "0..1" Servicio : define
Combinacion --> "0..*" Preset
Preset --> "1" Recurso : modelo principal
Preset --> "0..*" Recurso : companions / bind
Recurso --> "0..*" Instalacion
Lock --> Backend
Lock --> Recurso
Proyecto --> "0..1" Servicio : target
Combinacion --> "0..1" UltimaBuena : registro local
UltimaBuena --> Instalacion : protege
Instancia --> Instalacion : usa / protege
Proyecto *-- "0..*" Borrador
Proyecto --> "0..*" Schema
@enduml
```

Un servicio admite una instancia, no una por consumidor ni por combinación.
Los consumidores comparten proceso y conservan publicaciones independientes.
Compartir pesos por referencia dentro del proyecto no exige deduplicación global.
Instancias de otras carpetas deben respetar colisiones de recursos.

Los locks son transportables; inventario físico, PID, última buena y borradores son
locales. Los archivos INI/runtime se generan a partir del estado seleccionado;
no sustituyen el YAML ni se guardan en el temporal genérico del sistema.
