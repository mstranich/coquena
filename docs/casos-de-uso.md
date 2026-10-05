# Casos de uso

## Actores

- **Usuario:** una sola persona, en su propia PC. Opera Coquena por línea de comandos.
- **Cliente de inferencia** (secundario): OpenCode, Pi Code u otro harness. No habla con
  Coquena: consume el endpoint de `llama-server` y elige preset por nombre.
- **Distribución** (secundario): origen de releases de binarios; la oficial es
  `ggml-org/llama.cpp` en GitHub.

## Diagrama

```plantuml
@startuml
left to right direction
actor Usuario
actor "Cliente de\ninferencia" as Cliente
actor "Distribución oficial" as GH
actor "Hugging Face" as HF

rectangle Coquena {
  package Máquina {
    usecase "Inspeccionar hardware" as UC_HW
  }
  package Backends {
    usecase "Listar versiones remotas" as UC_BR
    usecase "Instalar build" as UC_BI
    usecase "Listar instaladas" as UC_BL
    usecase "Desinstalar backend" as UC_BD
    usecase "Registrar backend externo" as UC_BE
    usecase "Elegir familia\nsegún hardware" as UC_VAR
  }
  package Modelos {
    usecase "Descargar modelo\nelegir variante / ver README" as UC_DL
    usecase "Consultar novedades (update)" as UC_UP
    usecase "Aplicar actualización (upgrade)" as UC_UG
    usecase "Verificar integridad local" as UC_V
    usecase "Retirar modelo o companion" as UC_RM
    usecase "Registrar modelo externo" as UC_MR
    usecase "Registrar companion externo" as UC_CR
    usecase "Listar modelos" as UC_ML
  }
  package Configuración {
    usecase "Definir servidor (borrador)\n(presets, puerto,\nfamilia opcional)" as UC_ID
    usecase "Editar configuración\nglobal / de familia /\nde modelo" as UC_IG
    usecase "Definir / editar /\neliminar preset" as UC_PD
    usecase "Clonar configuración\no preset" as UC_PC
  }
  package "Descargas" {
    usecase "Consultar progreso y filtrar" as UC_DLIST
    usecase "Pausar / reanudar / cancelar" as UC_DCTRL
  }
  package "Instancias (borrador)" {
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
Usuario --> UC_BE
Usuario --> UC_DL
Usuario --> UC_UP
Usuario --> UC_UG
Usuario --> UC_V
Usuario --> UC_RM
Usuario --> UC_DLIST
Usuario --> UC_DCTRL
UC_DL --> HF
UC_UP --> HF
UC_UG --> HF
UC_MR --> HF : intento de emparejamiento
Usuario --> UC_MR
Usuario --> UC_CR
Usuario --> UC_ML
Usuario --> UC_ID
Usuario --> UC_IG
Usuario --> UC_PD
Usuario --> UC_PC
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

## Lectura del mapa

- Backends: selección, compatibilidad y comandos en [comandos.md](comandos.md#convenciones).
- Modelos: registro, emparejamiento, actualización e integridad en
  [modelos-y-companions.md](modelos-y-companions.md).
- Descargas: progreso, control y recuperación en [descargas.md](descargas.md).
- Configuración y presets: herencia y referencias en [modelo-de-datos.md](modelo-de-datos.md#dos-ejes-de-herencia).
- Servidores e instancias: borradores; todavía no autorizan implementación.
- Elegir ubicaciones distintas para configuración y archivos pesados se difiere; no es
  un caso de uso del catálogo de modelos.

El cliente de inferencia consume llama-server, no una API de Coquena.
La verificación local no usa red; sólo los casos remotos se relacionan con Hugging Face.
