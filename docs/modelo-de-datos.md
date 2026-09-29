# Modelo de datos

Diagrama de clases del dominio del núcleo. No define persistencia ni tecnología.

```plantuml
@startuml
hide empty methods

class Maquina <<contexto>> {
  sistemaOperativo
  arquitectura
}
class Dispositivo {
  tipo : CPU | GPU
  fabricante
  nombre
  memoria
  driver
}
class Backend {
  version : bNNNN
  variante
  ruta
}
class Modelo {
  nombre
  ruta(s) GGUF
}
class Companion {
  tipo : mmproj | draft | …
  ruta GGUF
}
class Preset {
  nombre <<único, visible al cliente>>
  contexto
  capasMoEEnCPU
  parámetros adicionales
}
class UsoDeCompanion {
  dispositivo / offload
}
class Instancia {
  nombre
  host
  puerto
  parámetros globales
  máximo de modelos cargados
}
class Ejecucion {
  pid
  inicio
  modo : primer plano | daemon
  rutaLog
}

Maquina "1" *-- "1..*" Dispositivo
Maquina "1" o-- "*" Backend : instalados
Backend "*" ..> "1..*" Dispositivo : puede usar
Modelo "0..*" -- "0..*" Companion : suele acompañar
Preset "*" --> "1" Modelo
Preset "1" *-- "*" UsoDeCompanion
UsoDeCompanion "*" --> "1" Companion
Instancia "*" --> "1" Backend
Instancia "*" o-- "1..*" Preset : ofrece
Instancia "1" *-- "0..1" Ejecucion : viva
@enduml
```

## Aclaraciones

- **Varios presets por modelo.** Dos presets pueden apuntar al mismo GGUF: uno rápido
  (sin companion, más capas MoE en CPU, contexto corto) y otro completo (con companion,
  contexto largo). El cliente elige cuál pedir.
- **El companion tiene su propia ubicación.** Un proyector puede alojarse en CPU mientras
  el modelo principal va a la GPU; por eso la relación pasa por `UsoDeCompanion`.
- **Instancia ↔ backend.** Una instancia se ata a un backend concreto (versión + variante).
  Dos instancias con backends distintos pueden convivir si usan puertos y dispositivos
  distintos.
- **Ejecución separada de instancia.** La instancia es configuración durable; la ejecución
  es efímera (PID, logs). Así se distingue "definida pero detenida" de "corriendo".

## Pendiente de confirmar

- ¿Un preset puede pertenecer a varias instancias (reutilizable), o es propio de una?
  El diagrama asume lo primero.
