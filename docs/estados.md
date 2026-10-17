# Diagramas de estado

Los ejes son independientes. Estar disponible, íntegro, actualizado, publicado
en el router y cargado en memoria son afirmaciones diferentes.

## Instancia y registro de ejecución

```plantuml
@startuml
[*] --> Detenida
Detenida --> Preparando : service start / intención válida
Preparando --> Iniciando : recursos preparados
Preparando --> Recuperando : fallo recuperable / daemon
Iniciando --> Activa : salud válida
Iniciando --> Recuperando : fallo / última buena elegible
Recuperando --> Activa : última buena sana / warning
Recuperando --> Fallida : sin recuperación / error
Activa --> Detenida : service stop explícito
Activa --> Preparando : reinicio explícito / preparar sin detener
Fallida --> [*] : informar / alertar / sin bucle
note right of Activa
service start informa si ya está activa.
Preparar otra versión conserva
el proceso hasta reinicio explícito.
end note
@enduml
```

Un fallo de integridad conocido no toma la transición de recuperación.
La política interactiva ofrece opciones; el diagrama representa el ciclo
desacoplado. Una pérdida del observador no es una transición del servidor.

## Backend registrado

Backend puede ser externo o gestionado. Registro, preparación, compatibilidad
de hardware, salud y uso activo se conservan por separado. Una revisión nueva
preparada no vuelve obsoleta físicamente la revisión que sigue usando el proceso.

## Modelo y companion (núcleo)

```plantuml
@startuml
state Disponibilidad {
  state Disponible
  state Incompleto
  state Inaccesible
  state Invalido
}
state Integridad {
  state SinReferencia
  state NoComprobada
  state Correcta
  state Fallida
}
state Seguimiento {
  state NoAplica
  state SinConsultar
  state SinCambios
  state ActualizacionDisponible
  state ConsultaFallida
  state RequiereIntervencion
}
@enduml
```

La comprobación completa de integridad no se repite al arrancar.
Un fallo registrado bloquea selecciones que usan ese recurso; excluirlo no lo repara.
Una retirada parcial conserva inventario/propiedad y no se considera servible.

## Preset publicado y estado observado del backend

Servible requiere recursos seleccionados disponibles y sin fallo de integridad
conocido. No garantiza que la carga real funcione.

```plantuml
@startuml
[*] --> NoPublicado
NoPublicado --> Publicado : up / apply compatible
Publicado --> Cargando : decisión del backend / petición
Cargando --> Cargado : resultado confirmado
Cargando --> Descargado : fallo / diagnóstico de entrada
Cargado --> Descargado : liberación por backend
Descargado --> Cargando : decisión del backend / petición
Publicado --> NoPublicado : down / withdraw
Descargado --> NoPublicado : down / withdraw
Cargado --> NoPublicado : retirar / liberar según backend
@enduml
```

Las transiciones de residencia son observaciones del backend, no comandos de Coquena.
Descargado aquí significa fuera de memoria, no borrado de disco.
No confundir servidor sano con todos los presets cargados correctamente.
