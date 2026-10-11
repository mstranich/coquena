# Definiciones declarativas

Esta modalidad corresponde a [M3](milestones/M3-definiciones-declarativas.md).
El YAML describe uno o varios servidores con nombre, su backend, sus presets,
modelos y parámetros. Permite expresar parámetros comunes a los presets de un
servidor; no crea valores globales del manager ni herencia entre servidores.
Es la fuente de la definición. Coquena conserva el estado de ejecución necesario
para detener por nombre los servidores iniciados desde ella.

## Recursos y archivos

La gestión de descargas de backends se limita a releases oficiales de llama.cpp.
Coquena prepara los builds oficiales y modelos faltantes antes de iniciar.
Alternativamente, el YAML referencia explícitamente un ejecutable externo local,
con cualquier nombre, sin buscarlo automáticamente dentro de una carpeta.
Ese ejecutable y sus bibliotecas permanecen bajo gestión del usuario: no se
descargan, actualizan ni retiran mediante las políticas de recursos gestionados.

Las rutas relativas declaradas en el YAML se resuelven desde la ubicación de ese
archivo, incluidas las referencias al ejecutable y al JSON complementario.
Esto permite trasladar una carpeta experimental completa. Esta base es distinta
de home, que sigue siendo la base de los directorios configurados del manager.

```text
experimento/
├── coquena.yaml
├── parametros.json
└── runtime/
    ├── servidor.exe
    └── bibliotecas necesarias
```

El JSON opcional contiene definiciones de parámetros, no repositorios ni binarios.
Coquena incorpora un catálogo inicial de parámetros oficiales, ampliable mediante
la gestión de parámetros personalizados para acompañar nuevas versiones.
La importación/exportación JSON permite compartir esas definiciones; el YAML
distribuye la definición de ejecución. No constituye un backup completo del manager.

## Validación

La validación estricta se configura a nivel del documento y puede sobrescribirse
por servidor. Si el servidor no la especifica, hereda la del documento; el valor
predeterminado es estricto. Este alcance no modifica la configuración global de Coquena.

En modo estricto, cada parámetro debe estar definido en el catálogo efectivo,
incluidas las definiciones JSON solicitadas por el YAML. Se valida su tipo y
aplicabilidad. Un parámetro desconocido se rechaza con su ubicación en el YAML,
antes de descargar recursos o iniciar procesos. Sin validación estricta, los
parámetros se transmiten sin exigir una definición en el catálogo.

Referenciar un ejecutable no adapta su interfaz. El usuario elige un build
compatible con la modalidad de invocación soportada por Coquena. El JSON describe
parámetros, no transforma argumentos ni implementa otro formato de configuración.
No se incorporan adaptadores de runtime ni proveedores de descargas de terceros.

## Definiciones pendientes

- Esquema y claves exactas del YAML y del JSON, selección de versiones y modelos.
- Sintaxis de inicio desde archivo y parada por nombre o conjunto de servidores.
- Colisiones de nombres y relación con las definiciones persistidas creadas por CLI.
- Fusión y conflictos al importar parámetros, alcance y persistencia de la importación.
- Compatibilidad requerida del ejecutable y diagnóstico de modalidades no soportadas,
  incluidos builds sin router para varios modelos.
- Comportamiento de parámetros conocidos pero no aplicables en modo no estricto.
- Recuperación y resultado cuando falla la preparación o el inicio de un conjunto.

Estos puntos requieren definición antes de preparar tarjetas de implementación.
