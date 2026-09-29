# Catálogo de parámetros de llama.cpp

Coquena conoce los parámetros (flags) de `llama-server` para validar lo que el usuario
configura y para saber en qué versión se puede usar cada uno.

## Los parámetros dependen de la versión, no de la familia

Un build para CPU y uno para CUDA del mismo release aceptan **los mismos argumentos**. Por
eso se puede, en un mismo lanzamiento, poner el modelo en GPU y el proyector multimodal en
CPU. Lo que cambia los parámetros es:

- **la versión:** un flag aparece en cierto build y puede desaparecer en otro;
- **la distribución:** un laboratorio que publica su propio llama.cpp puede sumar flags
  nuevos para sus funciones.

```plantuml
@startuml
hide empty methods

class Parametro {
  nombre canónico <<clave INI, sin guiones>>
  tipo : texto | entero | booleano | sin valor | enumerado
  alias corto
  variable de entorno
  descripción
}
class Disponibilidad {
  desde : build (opcional)
  hasta : build (opcional, excluido)
}
class Distribucion
class ValorEnumerado

Parametro "1" *-- "1..*" Disponibilidad
Disponibilidad "*" --> "1" Distribucion
Parametro "1" *-- "*" ValorEnumerado

note right of Disponibilidad
  Contra una versión concreta, un parámetro
  queda: admitido | no admitido | desconocido.
  Es consultivo: no impide configurarlo.
end note
@enduml
```

## Aclaraciones

- **Un solo nombre de identidad:** el nombre largo canónico, que es también la clave del
  INI. Los alias (`-c`, `LLAMA_ARG_CTX_SIZE`) se reconocen pero no son entradas aparte.
- **Consultivo:** un valor no admitido por la versión elegida se avisa al configurar y al
  lanzar; no se bloquea.
- **Consecuencia sobre la configuración de familia:** si los flags no varían por familia,
  la configuración por familia sirve para *valores* propios del hardware (dispositivo,
  capas en GPU), no para habilitar flags distintos.

## Arqueología (`legacy-go`)

`openspec/specs/llamacpp-field-catalog-persistence`: catálogo persistido en un documento
propio, nombre canónico + tipo inmutable, alias corto y de entorno, disponibilidad por
proveedor con rango `[desde, hasta)` de builds `b<dígitos>`, evaluación
`admitted | not-admitted | unknown`, y protección de parámetros referenciados por presets.

## Origen del catálogo

El catálogo **no se extrae automáticamente**: no hay heurística confiable ni forma de leer
el código de `llama-server`. Se distribuye a mano, como archivos que se comparten e
importan.

```plantuml
@startuml
|Autor del catálogo|
start
:Ejecutar llama-server --help\nde una versión concreta;
note right
  Puede hacerlo una persona
  o un agente de IA.
end note
:Redactar el archivo de catálogo\n(parámetros + disponibilidad);
:Compartir el archivo;
|Usuario|
:coquena param import <archivo>;
|Coquena|
if (¿el archivo es válido?) then (no)
  :Rechazar sin modificar\nel catálogo vigente;
  stop
endif
:Fusionar con el catálogo\n(por nombre canónico);
note right
  Si el parámetro ya existe,
  su disponibilidad se extiende
  con la del archivo importado.
end note
if (¿hay conflictos\n(tipo distinto, alias repetido)?) then (sí)
  :Informar y no aplicar\nesas entradas;
endif
:Guardar;
stop
@enduml
```
