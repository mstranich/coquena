# Diagramas de estado

## Instancia

El estado se **deriva** de la configuración, el PID guardado y lo que responde el
sistema operativo y el endpoint de salud de `llama-server`.

```plantuml
@startuml
[*] --> Detenida : definir instancia

Detenida --> Iniciando : iniciar\n[backend instalado,\npuerto libre]
Iniciando --> EnEjecucion : /health responde OK
Iniciando --> Fallida : el proceso termina\no vence el tiempo
EnEjecucion --> Deteniendo : detener
Deteniendo --> Detenida : el proceso terminó
EnEjecucion --> Fallida : el proceso muere\nsin que se pidiera
Fallida --> Iniciando : iniciar
Fallida --> Detenida : limpiar

EnEjecucion --> Huerfana : el PID guardado\nya no corresponde\na llama-server
Huerfana --> Detenida : limpiar PID

Detenida --> [*] : eliminar instancia

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

## Backend

```plantuml
@startuml
[*] --> Descargando : instalar
Descargando --> Instalado : descomprimido\ny sondeado (--version)
Descargando --> [*] : cancelar / error
Instalado --> EnUso : una instancia\nen ejecución lo usa
EnUso --> Instalado : la instancia se detiene
Instalado --> [*] : desinstalar
note right of EnUso
  No se puede desinstalar
  mientras está en uso.
end note
@enduml
```
