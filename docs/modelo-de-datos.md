# Modelo de datos

Relaciones del dominio, sin fijar persistencia ni clases C#. Ejecución sigue en borrador.

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
    repositorio de origen <<identificador>>
  }
  class Familia {
    nombre : cuda@12 | cuda@13 | vulkan | cpu | …
    aceleración
    versión de bibliotecas (si aplica)
  }
  class BackendRegistrado {
    id <<único, estable>>
    build oficial : bNNNN (si aplica)
    versión sondeada
    ruta
    origen : gestionado | externo
  }
}

package Modelos {
  class DirectorioDeModelos {
    ruta <<área gestionada; configuración posterior>>
  }
  class Modelo {
    nombre canónico <<catálogo local>>
    ruta(s) GGUF
    origen : gestionado | externo
  }
  class Companion {
    nombre canónico <<catálogo local>>
    tipo : mmproj | draft | …
    ruta(s) GGUF
    origen : gestionado | externo
  }
}

package "Configuración (eje backend)" {
  abstract class Configuracion {
    valores : parámetro → literal o referencia de archivo
  }
  class ConfiguracionGlobal <<única>>
  class ConfiguracionDeFamilia
}

package "Configuración (eje modelo)" {
  class ConfiguracionDeModelo
  class Preset {
    nombre <<visible al cliente>>
  }
}

package Ejecución {
  class Servidor {
    nombre
    puerto
  }
  class Instancia {
    id
    puerto efectivo
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
Familia "1" *-- "*" BackendRegistrado : registrados
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
Configuracion "*" --> "*" Companion : referencia en un parámetro

Servidor "*" --> "0..1" Familia : fija (opcional)
Servidor "*" o-- "1..*" Preset : ofrece
Instancia "*" --> "1" Servidor : lanza
Instancia "*" --> "1" BackendRegistrado : ejecuta
Instancia "1" *-- "1" ArchivoINI : genera
note bottom of Instancia
  Sin filtro explícito, el build compatible
  más reciente; entre candidatos de ese build,
  preferencia de backend.
end note
@enduml
```

La configuración de un modelo puede estar vacía. Antes de implementar presets debe
definirse cómo se representa o crea su raíz de herencia; la cardinalidad opcional no
autoriza a dejar un preset sin configuración efectiva.

## Catálogo y seguimiento: estructura conceptual propuesta

Los campos y significados se definen una sola vez en
[la guía técnica](implementacion-catalogo-modelos.md#2-datos-mínimos-y-significado).

```plantuml
@startuml
hide empty methods
class EntradaDeCatalogo {
  nombre canónico
  propiedad : gestionado | externo
}
class Instalacion
class ArchivoLocal
class OrigenSeguido
class ConsultaRemota
class PlanDeActualizacion
EntradaDeCatalogo "1" --> "1" Instalacion : activa
Instalacion "1" o-- "1..*" ArchivoLocal
EntradaDeCatalogo "1" --> "0..1" OrigenSeguido : sólo gestionados
OrigenSeguido --> ConsultaRemota : observa
ConsultaRemota --> "0..1" PlanDeActualizacion : si resuelto
PlanDeActualizacion --> Instalacion : parte de
note right of EntradaDeCatalogo
  Modelo o companion.
  Emparejar un externo no crea seguimiento.
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
  card "Familia\n(p. ej. cuda@13)" as F
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
- **Precedencia:** el valor específico de un preset sustituye al mismo parámetro de
  `[*]`; la documentación actual da prioridad a los argumentos CLI sobre ambos.
  Coquena evita duplicar en CLI valores configurables por preset y verifica la regla
  para el release elegido. Coquena emite una sola
  clave canónica por parámetro y sección, tras aplanar cada eje. Los parámetros que
  admitan varios valores y los que sólo tengan sentido para el proceso router requieren
  verificación específica por versión antes de fijar su tratamiento.
  **No se presume herencia aditiva:** si un parámetro admite una lista, eso no demuestra
  que repetir su clave en dos ámbitos acumule elementos.
- **Eliminar un valor (`unset`):** quita sólo el par clave-valor propio del ámbito,
  sin modificar el padre ni bloquear su herencia. El valor heredado vuelve a regir;
  si no existe, no se emite la clave en esa sección del INI.
- **Companions:** se registran independientemente y se referencian mediante parámetros
  de configuración. El preset hereda o sobrescribe esas referencias; no existe una lista
  paralela de asociaciones. El render resuelve las referencias efectivas a rutas absolutas.
  La ubicación CPU/GPU se expresa con los parámetros correspondientes del backend.
  `config set <param> --ref <nombre>` guarda una referencia; el valor posicional es
  literal, incluso si coincide con un nombre registrado. Son alternativas excluyentes.
  La compatibilidad funcional del conjunto queda a cargo del backend; Coquena no
  necesita un modo forzado para configurar esas referencias.

## Aclaraciones

- **Servidor** es la composición que Coquena coordina: un puerto, opcionalmente una
  familia, y los
  presets que ofrece. Su diseño y sus comandos siguen pendientes de revisión.
- **Clonar** configuraciones y presets hacia otros modelos o familias es la forma de
  reutilizar un ajuste fino.
  En un preset se copian sólo sus valores propios, incluidas las referencias a companions,
  no los valores heredados ni los archivos. El clon hereda del modelo destino y no queda
  vinculado al preset origen; cambios posteriores de éste no se propagan al clon.
- **Propiedad de archivos.** Coquena descarga modelos al directorio gestionado y borra
  sus archivos al retirarlos; un registro externo sólo guarda rutas y nunca autoriza a
  mover o borrar esos archivos. La regla es la misma para backends. Un modelo puede ser
  un conjunto de shards, no una fila por archivo.
- **Backend externo.** La relación con `Familia` procede de la declaración del usuario;
  también se declara la distribución de origen (repositorio), que permite asociar el
  catálogo por origen y release. Omitirla equivale al origen oficial
  `https://github.com/ggml-org/llama.cpp`; un compilado de terceros debe indicar el suyo.
  La versión sondeada procede de `llama-server --version`; sólo los releases oficiales
  llevan necesariamente un build `bNNNN`. Ese sondeo no verifica la familia ni la
  compatibilidad de aceleración. Cada backend registrado recibe un ID único y estable;
  `familia + build` es sólo un atajo cuando resuelve a uno.
- **Instancias.** Cada lanzamiento tiene ID y puerto efectivos propios; el puerto del
  servidor es sólo el valor predeterminado. Dos instancias no comparten puerto ni INI.
- **Distribuciones de backends.** Sólo `ggml-org` es origen de descubrimiento y
  descarga de binarios. Hugging Face es el origen de modelos, no esta distribución.
  Un tercero puede aportar un binario, pero el usuario lo registra como
  externo y lo selecciona explícitamente. Agregar repositorios de terceros como
  orígenes de confianza queda para después. Identificarlos para asociar parámetros no
  implica consultarlos, descargar de ellos ni verificar la procedencia del binario.

## Render del INI

El render recibe la configuración efectiva y las rutas ya resueltas del catálogo.
No consulta la red. Se propone como función determinista: las mismas entradas producen
el mismo INI, usando claves canónicas sin guiones. Su implementación y pruebas deben
cubrir la herencia y las reglas del backend seleccionado.
