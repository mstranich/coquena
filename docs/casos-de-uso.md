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
actor "Distribución" as GH

rectangle Coquena {
  package Máquina {
    usecase "Inspeccionar hardware" as UC_HW
  }
  package Backends {
    usecase "Listar versiones remotas" as UC_BR
    usecase "Instalar versión" as UC_BI
    usecase "Listar instaladas" as UC_BL
    usecase "Desinstalar versión" as UC_BD
    usecase "Registrar backend externo" as UC_BE
    usecase "Elegir familia\nsegún hardware" as UC_VAR
    usecase "Incorporar distribución" as UC_DI
  }
  package Modelos {
    usecase "Configurar directorio\nde modelos" as UC_MD
    usecase "Registrar modelo externo" as UC_MR
    usecase "Registrar companion externo" as UC_CR
    usecase "Listar modelos" as UC_ML
  }
  package Configuración {
    usecase "Definir servidor\n(presets, puerto,\nfamilia opcional)" as UC_ID
    usecase "Editar configuración\nglobal / de familia /\nde modelo" as UC_IG
    usecase "Definir / editar /\neliminar preset" as UC_PD
    usecase "Clonar configuración\no preset" as UC_PC
  }
  package Instancias {
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
Usuario --> UC_DI
Usuario --> UC_MD
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

## Notas por caso

**Instalar versión.** Por defecto Coquena detecta el hardware y propone la mejor familia
(p. ej. `cuda13` si el driver NVIDIA lo soporta, si no `cuda12`, si no `vulkan`, si no
`cpu`). El usuario siempre puede ser explícito y pedir otra, incluida `cpu` en una máquina
con GPU. En Windows, la familia CUDA requiere además el paquete `cudart` del mismo
release (arqueología de `legacy-go`, `internal/runtime/llamacpp/catalog.go`).

**Incorporar distribución.** Además de `ggml-org`, un desarrollador o laboratorio puede
publicar sus propios binarios compatibles; Coquena los gestiona como SDKMan gestiona el JDK
de Oracle junto a otros.

**Registrar backend externo.** El usuario puede incorporar un binario que compiló o
instaló por su cuenta. Coquena registra su ubicación para usarlo, pero no lo mueve ni
lo borra al retirarlo. En cambio, al desinstalar un backend descargado por Coquena se
borran sus archivos y se informan los bytes liberados. La sintaxis del comando de
registro todavía no está fijada.

**Varias familias a la vez.** En una máquina con GPU NVIDIA y AMD pueden correr dos
instancias simultáneas: una con `cuda13` y otra con `vulkan`, en puertos distintos.

**Registrar modelo externo.** Coquena guarda la ruta del GGUF sin copiarlo ni moverlo. El
directorio de modelos es sólo el destino de lo que Coquena descarga (iteración posterior).
Un modelo puede comprender varios shards; registrarlo y retirarlo se hace como unidad.
Retirar uno externo deja sus archivos intactos; retirar uno gestionado borra todos sus
archivos propios e informa el espacio liberado.

**Clonar configuración o preset.** Reutiliza un ajuste fino en otro modelo o familia.

**Iniciar instancia.** Sin backend explícito, Coquena elige la familia instalada más
adecuada al hardware y su última versión. Arranca aunque falten archivos de algún preset
(avisa y lo omite) mientras quede al menos uno alcanzable. Coquena genera el INI en ese
momento, dentro de su home. Como
`docker run`: en primer plano queda adjunto mostrando logs; en modo daemon se desacopla y
Coquena guarda el PID para gestionarla después.
