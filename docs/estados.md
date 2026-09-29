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
