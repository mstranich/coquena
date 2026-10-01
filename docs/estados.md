# Diagramas de estado

## Instancia

Una instancia es un `llama-server` vivo: existe desde que se inicia hasta que termina. El
estado se **deriva** del PID guardado, de lo que responde el sistema operativo y del
endpoint de salud de `llama-server`.

```plantuml
@startuml
[*] --> Iniciando : iniciar servidor + versión\n[versión de la familia,\npuerto libre]\n/ genera el INI
Iniciando --> EnEjecucion : /health responde OK
Iniciando --> Fallida : el proceso termina\no vence el tiempo
EnEjecucion --> Deteniendo : detener
Deteniendo --> [*] : el proceso terminó
EnEjecucion --> Fallida : el proceso muere\nsin que se pidiera
Fallida --> [*] : limpiar

EnEjecucion --> Huerfana : el PID guardado\nya no corresponde\na llama-server
Huerfana --> [*] : limpiar PID

state EnEjecucion {
  [*] --> PrimerPlano
  [*] --> Daemon
  PrimerPlano : adjunta a la terminal,\nmuestra logs
  Daemon : desacoplada,\nPID guardado
}
@enduml
```

- **Huérfana:** el PID guardado apunta a un proceso que ya no existe o que el sistema
  reutilizó para otro programa. Coquena debe verificar que el PID pertenece al
  `llama-server` que lanzó antes de actuar sobre él.

## Versión de backend

```plantuml
@startuml
[*] --> Descargando : instalar
Descargando --> Instalada : descomprimida\ny sondeada (--version)
Descargando --> [*] : cancelar / error
Instalada --> EnUso : una instancia\nen ejecución la usa
EnUso --> Instalada : la instancia termina
Instalada --> [*] : desinstalar
note right of EnUso
  No se puede desinstalar
  mientras está en uso.
end note
@enduml
```

## Modelo y companion (núcleo)

Un solo eje: **disponibilidad**. Se deriva en cada consulta, sin hashear y sin persistir.
Por qué un solo eje, y qué quedó afuera: [modelos-y-companions.md](modelos-y-companions.md).

```plantuml
@startuml
[*] --> Registrado : model add / add-companion\n[ruta válida, GGUF legible]

state Registrado {
  state q <<choice>>
  [*] --> q : cada consulta\n(list, run, preset)
  q --> Inaccesible : la ruta no existe\no no se puede leer
  q --> Invalido : no es un GGUF legible
  q --> Incompleto : faltan shards
  q --> Disponible : todo presente\ny legible
}

Registrado --> [*] : model remove\n(borra bytes propios;\nconserva externos)

note right of Disponible
  Si tamaño o fecha difieren
  de lo registrado: se avisa.
  No se actualiza el registro
  ni se verifica integridad.
end note
@enduml
```

- **Origen** (gestionado o externo) es un atributo del registro, no un estado.
- Un companion no depende de su modelo principal para estar disponible.

## Preset

Derivado de la disponibilidad de su modelo y sus companions.

```plantuml
@startuml
state q <<choice>>
[*] --> q
q --> Servible : modelo y companions\ndisponibles
q --> NoServible : algún archivo no disponible\no referencia retirada del registro
NoServible : al iniciar el servidor,\nse omite con aviso
@enduml
```
