# Casos de uso

## Actores

- Usuario local o remoto por SSH: define, prueba y opera su proyecto.
- DevOps: reproduce una receta en hardware compatible, configura logs y alertas.
- Frontend CLI/TUI/Web: invoca operaciones comunes y observa APIs/procesos.
- Backend: sirve peticiones y ofrece las capacidades de su build.

## Diagrama

```plantuml
@startuml
left to right direction
actor Usuario
actor DevOps
actor Backend
rectangle Coquena {
  usecase "Inicializar y editar proyecto" as Config
  usecase "Importar servicio / receta global" as Import
  usecase "Publicar / retirar conjunto consumidor" as Publish
  usecase "Resolver YAML y overrides" as Resolve
  usecase "Preparar recursos y locks" as Prepare
  usecase "Iniciar / detener / recuperar" as Run
  usecase "Aplicar presets y cargar memoria" as Apply
  usecase "Observar salud, logs y métricas" as Observe
  usecase "Comparar y medir rendimiento" as Bench
  usecase "Actualizar, verificar y limpiar" as Maintain
  usecase "Compartir receta y reproducir" as Share
}
Usuario --> Import
Usuario --> Publish
Usuario --> Config
Usuario --> Run
Usuario --> Apply
Usuario --> Observe
Usuario --> Bench
Usuario --> Maintain
DevOps --> Share
DevOps --> Run
DevOps --> Observe
Config --> Resolve
Import --> Resolve
Publish --> Prepare
Publish --> Backend
Resolve --> Prepare
Run --> Prepare
Run --> Backend
Apply --> Backend
Observe --> Backend
Share --> Resolve
@enduml
```

## Lectura del mapa

Editar, preparar y aplicar son efectos distintos. La interacción visual puede
crear borradores y experimentar antes de guardar, manteniendo explícita la
composición activa. Un router compatible puede recibir presets sin reinicio;
cambiar el backend requiere otro proceso.

La observación no inicia modelos ni deja un supervisor. El benchmarking tiene
su propio ciclo explícito y no debe interrumpir una instancia productiva sin
una operación acordada. Detalles en [interfaces](interfaces-visuales.md),
[observabilidad](observabilidad.md) y [benchmarking](benchmarking.md).
