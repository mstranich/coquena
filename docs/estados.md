# Diagramas de estado

## Instancia y registro de ejecución

El estado se deriva de la existencia e identidad del proceso. Salud y modo de ejecución
son observaciones distintas: no responder a /health no demuestra que el proceso terminó.
Los tiempos y mecanismos concretos se definen al implementar.

```plantuml
@startuml
[*] --> Registrada : inicio o lectura de registro local
Registrada --> Activa : proceso vivo e identidad confirmada
Registrada --> IdentidadIncierta : no se puede verificar
Registrada --> Terminada : ejecución anterior confirmada como terminada
Activa --> Deteniendo : stop, Ctrl+C en primer plano o restart
Activa --> Terminada : proceso termina
Activa --> IdentidadIncierta : no se puede verificar identidad
IdentidadIncierta --> Activa : identidad y proceso vivo confirmados
IdentidadIncierta --> Terminada : ejecución original confirmada como terminada
Deteniendo --> Terminada : terminación confirmada
Deteniendo --> Activa : fallo de detención; proceso sigue vivo
Terminada --> [*] : limpiar registro activo e INI; conservar log
IdentidadIncierta : diagnóstico; no matar procesos ni iniciar sustituto
Activa : primer plano o segundo plano
Activa : salud observada por separado
@enduml
```

Un PID reutilizado no autoriza a actuar sobre el proceso ajeno. Si se confirma que la
ejecución original terminó, se limpia su registro, no se mata el proceso que reutilizó
el PID. Si hay incertidumbre, se conserva evidencia y se informa. No hay reinicio automático.
La definición de servidor con backend ausente es una referencia rota, no un proceso huérfano.

## Backend registrado

La transferencia y sus estados pertenecen a [descargas.md](descargas.md).
El backend aparece como registrado sólo tras validar y publicar la instalación, o tras
registrar un externo con sondeo válido. Un trabajo pausado o fallido no es un backend
instalado y permanece recuperable en el administrador de descargas.

Retirar un backend se rechaza mientras una instancia activa lo utiliza. Si sólo lo
referencian definiciones de servidores detenidos, se permite retirarlo y se conservan
esas definiciones con backend ausente. Véase [servidores.md](servidores.md#backend-seleccionado-ausente).

## Modelo y companion (núcleo)

Este diagrama representa sólo **disponibilidad**, derivada en cada consulta sin hash
completo y sin persistir. Seguimiento remoto e integridad son ejes independientes,
definidos en [modelos-y-companions.md](modelos-y-companions.md): perder el seguimiento
no cambia por sí solo la disponibilidad; en externos el seguimiento no aplica.

```plantuml
@startuml
[*] --> Registrado : alta externa legible (puede estar incompleta)\no descarga gestionada publicada completa

state Registrado {
  state q <<choice>>
  [*] --> q : cada consulta\n(list, server start, preset)
  q --> Inaccesible : la ruta no existe\no no se puede leer
  q --> Invalido : GGUF ilegible o metadatos\nde división contradictorios
  q --> Incompleto : faltan shards
  q --> Disponible : todo presente\ny legible
}

Registrado --> [*] : remove del modelo o companion\n(borra bytes propios;\nconserva externos)

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

## Preset: disponibilidad de referencias

Derivado de la disponibilidad de su modelo y sus companions.

```plantuml
@startuml
state q <<choice>>
[*] --> q
q --> Servible : modelo y companions\ndisponibles
q --> NoServible : algún archivo no disponible\no referencia retirada del registro
NoServible : alguna referencia no puede resolverse
@enduml
```
