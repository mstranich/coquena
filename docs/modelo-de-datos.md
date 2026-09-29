# Modelo de datos

Diagrama de clases del dominio del núcleo. No define persistencia ni tecnología.

Coquena tiene **su propio modelo interno**. Los archivos INI de `llama-server` no son
parte de él: se **generan** en cada lanzamiento, a partir de ese modelo, y duran lo que
dura la instancia — como los archivos que IntelliJ IDEA arma en su propia carpeta al
lanzar una aplicación para depurarla. Se guardan en el home de Coquena, nunca en la
carpeta temporal del sistema operativo.

```plantuml
@startuml
hide empty methods

package Máquina {
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
}

package Backends {
  class Distribucion {
    nombre : ggml-org | …
    origen de releases
  }
  class Familia {
    nombre : cuda12 | cuda13 | vulkan | cpu | …
    aceleración
  }
  class VersionBackend {
    build : bNNNN
    ruta
  }
}

package Modelos {
  class DirectorioDeModelos {
    ruta <<definida por el usuario>>
  }
  class Modelo {
    ruta(s) GGUF
    origen : gestionado | externo
  }
  class Companion {
    tipo : mmproj | draft | …
    ruta GGUF
    origen : gestionado | externo
  }
}

package "Configuración (eje backend)" {
  abstract class Configuracion {
    valores : parámetro → valor
  }
  class ConfiguracionGlobal <<única>>
  class ConfiguracionDeFamilia
}

package "Configuración (eje modelo)" {
  class ConfiguracionDeModelo
  class Preset {
    nombre <<visible al cliente>>
  }
  class UsoDeCompanion {
    ubicación (GPU / CPU)
  }
}

package Ejecución {
  class Servidor {
    nombre
    puerto
  }
  class Instancia {
    pid
    modo : primer plano | daemon
    inicio
    rutaLog
  }
  class ArchivoINI <<generado por lanzamiento>> {
    ruta : <home>/run/<servidor>/<id>.ini
  }
}

Maquina "1" *-- "1..*" Dispositivo
Distribucion "1" *-- "1..*" Familia
Familia "1" *-- "*" VersionBackend : instaladas
Familia ..> Dispositivo : acelera

DirectorioDeModelos "1" o-- "*" Modelo : gestionados
DirectorioDeModelos "1" o-- "*" Companion : gestionados

Configuracion <|-- ConfiguracionGlobal
Configuracion <|-- ConfiguracionDeFamilia
Configuracion <|-- ConfiguracionDeModelo
Configuracion <|-- Preset
ConfiguracionDeFamilia --> ConfiguracionGlobal : hereda de
ConfiguracionDeFamilia "0..1" --> "1" Familia
ConfiguracionDeModelo "0..1" --> "1" Modelo
Preset "*" --> "1" ConfiguracionDeModelo : hereda de
Preset "1" *-- "*" UsoDeCompanion
UsoDeCompanion "*" --> "1" Companion

Servidor "*" --> "0..1" Familia : fija (opcional)
Servidor "*" o-- "1..*" Preset : ofrece
Instancia "*" --> "1" Servidor : lanza
Instancia "*" --> "1" VersionBackend : ejecuta
Instancia "1" *-- "1" ArchivoINI : genera
note bottom of Instancia
  Sin familia explícita, la más adecuada
  al hardware; sin versión, la última
  instalada de esa familia.
end note
@enduml
```

## Dos ejes de herencia

La configuración se hereda por dos ejes que se encuentran en el INI generado:

```plantuml
@startuml
left to right direction
rectangle "Eje backend" {
  card "Global\n(todos los backends)" as G
  card "Familia\n(p. ej. cuda13)" as F
  G --> F : hereda
}
rectangle "Eje modelo" {
  card "Modelo\n(configuración inicial)" as M
  card "Preset\n(comportamiento específico)" as P
  M --> P : hereda
}
rectangle "INI generado" {
  card "[*]" as S
  card "[nombre-del-preset]" as N
}
F --> S : aplana
P --> N : aplana
S ..> N : llama-server\nhereda
@enduml
```

- **Eje backend:** una configuración aplicable a cualquier backend, especializada por
  familia. Se vuelca en la sección `[*]`.
- **Eje modelo:** una configuración inicial del modelo, especializada en presets para un
  comportamiento concreto (rápido, contexto largo, con o sin companion). Cada preset se
  vuelca en su propia sección.
- **Precedencia (supuesto):** como `llama-server` hace que una sección de preset pise a
  `[*]`, el eje modelo gana sobre el eje backend cuando ambos definen el mismo parámetro.

## Aclaraciones

- **Servidor** es la composición que Coquena coordina: un puerto, opcionalmente una
  familia, y los
  presets que ofrece. Es lo que antes se escribía a mano en scripts lanzadores. El nombre
  retoma `data/servers/` de `legacy-go`; es provisorio.
- **Clonar** configuraciones y presets hacia otros modelos o familias es la forma de
  reutilizar un ajuste fino.
- **Modelos: nunca se copian.** Coquena descarga (iteración posterior) al directorio de
  modelos, y registra modelos externos por ruta, como SDKMan registra un JDK instalado a
  mano.
- **Distribuciones extensibles.** Además de `ggml-org`, un tercero puede incorporar sus
  familias.

## Arqueología (`legacy-go`)

- Presets en `config/model-presets.json`, renderizados a un INI por runtime. El render es
  una función pura: determinista, sin red, byte a byte idéntico ante las mismas entradas.
  Las claves se emiten con el nombre largo canónico, sin guiones.
- Nombre de preset = sección INI = identificador del modelo servido: minúsculas,
  segmentos separados por `-` o `.`; reservados `global` y `render`.
- Un preset podía declarar qué runtimes lo admiten (familia + rango de releases), y
  Coquena calculaba el rango mínimo según los parámetros usados (un parámetro aparece en
  cierto release). Era consultivo: no bloqueaba nada.
- Sin versión explícita se usa la última (`latest`), como `fnm install --lts`.
  (`openspec/specs/model-preset-persistence`, `llamacpp-runtime-artifact-management`)
