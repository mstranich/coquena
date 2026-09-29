# Casos de uso

## Actores

- **Usuario:** una sola persona, en su propia PC. Opera Coquena por línea de comandos.
- **Cliente de inferencia** (secundario): OpenCode, Pi Code u otro harness. No habla con
  Coquena: consume el endpoint de `llama-server` y elige preset por nombre.
- **Repositorio de releases de llama.cpp** (secundario): GitHub, de donde salen los
  binarios.

## Diagrama

```plantuml
@startuml
left to right direction
actor Usuario
actor "Cliente de\ninferencia" as Cliente
actor "Releases de\nllama.cpp" as GH

rectangle Coquena {
  package Máquina {
    usecase "Inspeccionar hardware" as UC_HW
  }
  package Backends {
    usecase "Listar backends remotos" as UC_BR
    usecase "Instalar backend" as UC_BI
    usecase "Listar backends instalados" as UC_BL
    usecase "Desinstalar backend" as UC_BD
    usecase "Elegir variante\nsegún hardware" as UC_VAR
  }
  package Modelos {
    usecase "Registrar modelo local" as UC_MR
    usecase "Registrar companion" as UC_CR
    usecase "Listar modelos" as UC_ML
  }
  package Presets {
    usecase "Definir preset" as UC_PD
    usecase "Editar / eliminar preset" as UC_PE
  }
  package Instancias {
    usecase "Definir instancia" as UC_ID
    usecase "Iniciar instancia" as UC_IS
    usecase "Detener instancia" as UC_IX
    usecase "Ver estado (ps)" as UC_IP
    usecase "Seguir logs" as UC_IL
  }
}

Usuario --> UC_HW
Usuario --> UC_BR
Usuario --> UC_BI
Usuario --> UC_BL
Usuario --> UC_BD
Usuario --> UC_MR
Usuario --> UC_CR
Usuario --> UC_ML
Usuario --> UC_PD
Usuario --> UC_PE
Usuario --> UC_ID
Usuario --> UC_IS
Usuario --> UC_IX
Usuario --> UC_IP
Usuario --> UC_IL

UC_BR --> GH
UC_BI --> GH
UC_BI ..> UC_VAR : <<include>>
UC_VAR ..> UC_HW : <<include>>
UC_IS ..> UC_IL : <<extend>>\n(primer plano)

Cliente --> (Consumir preset\npor nombre)
note right of (Consumir preset\npor nombre)
  Lo atiende llama-server,
  no Coquena.
end note
@enduml
```

## Notas por caso

**Instalar backend.** Por defecto Coquena detecta el hardware y propone la mejor variante
(p. ej. `cuda@13` si el driver NVIDIA lo soporta, si no `cuda@12`, si no `vulkan`, si no
`cpu`). El usuario siempre puede ser explícito y pedir otra, incluida `cpu` en una máquina
con GPU. En Windows, la variante CUDA requiere además el paquete `cudart` del mismo
release (hallazgo de `legacy-go`, `internal/runtime/llamacpp/catalog.go`).

**Varios backends a la vez.** En una máquina con GPU NVIDIA y AMD pueden correr dos
instancias simultáneas: una con backend CUDA y otra con Vulkan/HIP, en puertos distintos.

**Iniciar instancia.** Como `docker run`: en primer plano queda adjunto mostrando logs;
con modo daemon se desacopla y Coquena guarda el PID para gestionarla después.
