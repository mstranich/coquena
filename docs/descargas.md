# Administrador de descargas

## Propósito y alcance

Gestiona transferencias de modelos, companions y backends del ámbito seleccionado. El motor
transfiere una selección concreta; no decide qué modelo sustituye a otro ni cambia
la familia del backend. La selección y la publicación tienen reglas propias del recurso.

## Responsabilidades

- Selección: fija origen, revisión y archivos antes de transferir.
- Operación: agrupa una instalación o preparación con identificador estable.
- Transferencia: informa cantidad transferida, total, velocidad, diagnóstico y continuidad por archivo.
- Publicación: incorpora sólo resultados completos y verificados.
- Inventario: registra propiedad, revisión física y origen explícito o automático.

Una descarga explícita no incorpora todos los companions del repositorio ni edita
el YAML. Preparar una combinación incluye los recursos que ésta declara.
Un conjunto de shards es una unidad completa; un backend puede necesitar varios
artefactos y bibliotecas. Terminar un archivo no completa necesariamente una unidad.

## Identificación y persistencia

Cada operación tiene un GUID, persistido antes de transferir y mostrado al usuario.
Conserva selección fijada, revisión, archivos esperados, destinos, parciales, bytes
confirmados, tamaños, identidad de contenido, validadores HTTP, fechas, fase,
diagnóstico e identidad del ejecutor. El nombre del recurso no sustituye ese GUID.

No persistir credenciales ni usar URLs firmadas caducables como identidad.
Renovar una URL no autoriza cambiar el contenido seleccionado. El catálogo instalado
y una preparación en curso son estados distintos.

### Concurrencia

Un trabajo tiene un solo ejecutor. Comprobar identidad del proceso, no sólo PID.
Terminales distintas pueden operar; la publicación del mismo destino y las escrituras
de inventario se excluyen y vuelven a validar para no perder cambios. No mantener
un bloqueo de catálogo durante toda una transferencia. Ninguna operación adquiere
por accidente archivos externos ni sobrescribe otra preparación.

### Datos de la operación

El progreso es por archivo y por unidad. Las interfaces pueden observar y coordinar
operaciones sin que el motor dependa de una terminal concreta.
La selección conserva commit de modelos o release/origen/variante del backend,
separados de la identidad de bytes y de los verificadores del transporte.

### Área de preparación acordada

La preparación pertenece al ámbito seleccionado, separada de los recursos publicados y de
archivos externos. Su ubicación y documentos internos se resuelven dentro del
estado local de .coquena; no constituyen una estructura pública definitiva.

Persistir checkpoints mediante reemplazo seguro y con recuperación ante interrupción.
Pausar conserva parciales. Cancelar elimina sólo preparación propia después de
detener sus escritores. Una operación publicada no se revierte borrando su destino
como si todavía fuese un parcial. Tras completar la limpieza, retirar la operación
terminal; conservar un registro acotado de transacciones, sin exigir event sourcing.

## Estados y fases

Distinguir selección, transferencia, pausa/interrupción, verificación, preparación,
publicación y limpieza. Error, cancelación y éxito no deben confundirse con progreso
de bytes. Una transferencia al 100 % puede fallar en verificación o extracción.

## Comportamiento por acción

### Iniciar y consultar

Crear el trabajo sólo después de una selección válida y sin ambigüedad.
Consultar informa estado y progreso sin continuar trabajos silenciosamente.
Descarga manual implica protección explícita del recurso; una descarga por intención
YAML o upgrade tiene origen automático. Reutilizar una instalación válida ya disponible
no obliga a descargarla otra vez para marcarla explícita.

### Pausar

Ctrl+C interrumpe de forma recuperable y conserva selección y checkpoints.
No equivale a cancelar ni a borrar lo descargado. Al finalizar la CLI no queda un
descargador Coquena residente implícito.

### Reanudar

Continuar el mismo contenido y GUID. Comprobar tamaño real del parcial y checkpoint:
un parcial menor no puede declararse completo; bytes adicionales sólo se aceptan
tras reconciliación verificable.

Una continuación exige respuesta 206 y Content-Range coherente con desplazamiento,
total y selección. Nunca anexar una respuesta 200 ni mezclar revisiones. Si el
servidor no admite rangos o cambió la identidad, diagnosticarlo y ofrecer reiniciar
ese archivo con la misma selección; no descartar parciales por una decisión
destructiva implícita en una ejecución sin interacción.

Recalcular la integridad sobre prefijo y sufijo completos antes de publicar.
ETag, commit y hash SHA-256 de bytes no son intercambiables.

### Cancelar

Detener al ejecutor antes de retirar sus parciales. Borrar sólo preparación propia,
manteniendo inventario suficiente para reintentar limpieza si falla.
Cancelar después de publicar no elimina una instalación ya incorporada.

### Recuperar después de un cierre

Reconocer ejecutores vivos antes de marcar interrupción. Una preparación recuperada
no se publica por existir en disco: se comprueba su estado, selección e integridad.
Los fallos de lectura de metadatos persistidos se diagnostican, no se omiten.

## Validación específica por recurso

### Modelos

Descargar todos los shards desde el mismo commit. Comprobar selección, tamaños,
cabeceras/división e integridad completa por archivo. Conservar SHA-256 como referencia
local y contrastar identidad remota cuando su algoritmo sea comparable.
No fusionar shards ni publicar una mezcla de revisiones. Aplicar las mismas garantías
a cada companion declarado.

### Backends

Preparar todos sus artefactos; verificar transferencia, extracción e integridad.
Conservar referencias de integridad para verificaciones posteriores del backend.
Rechazar entradas de archivo que escapen del destino y limitar la extracción.
Comprobar ejecutable y bibliotecas requeridos antes de declararlo preparado.

La identificación/capacidad depende del backend. No exigir a todo fork parámetros,
endpoints o una respuesta --version del oficial. Ser un archivo ejecutable no
certifica compatibilidad con el hardware ni funcionamiento del servidor.

## Errores y seguridad del motor

La versión anterior permanece disponible ante preparación incompleta. Verificar
antes de publicar y nunca sobrescribir recursos en uso. Si el conjunto corresponde
a un upgrade, todas las unidades seleccionadas deben prepararse antes de publicar
sus locks; un fallo no activa parcialmente ese conjunto. Las preparaciones completas
pueden conservarse para recuperación.

Coordinar inventario y archivos mediante protocolo recuperable. Un rename no vuelve
atómica una operación que cruza varios archivos o volúmenes. Si hace falta copiar,
verificar destino antes de cambiar referencias y retirar preparación.

En Windows, bloqueos transitorios al publicar admiten reintentos acotados con espera
y revalidación. No convertir cualquier error de permisos, espacio o integridad en
reintentos indefinidos. Conservar diagnóstico y recursos anteriores.

## Superficie de comandos

La CLI y las interfaces usan las mismas operaciones de consulta, pausa, reanudación
y cancelación. Descarga no equivale a aplicación sobre la instancia.
Véanse [comandos](comandos.md#download),
[actualización y limpieza](actualizacion-y-limpieza.md) y
[servidores](servidores.md#recuperación-y-fallos).

## Pruebas de aceptación para implementar desde cero

El contrato exige evidencia de interrupción/reanudación, rangos incorrectos,
cambio de contenido remoto, shards incompletos, hash incorrecto, extracción insegura,
falta de espacio, publicación interrumpida y limpieza parcial. Debe comprobarse que
los fallos conservan la instalación anterior y no afectan archivos vecinos/externos.

## Implementación y pendientes

El mecanismo de exclusión y publicación sigue [persistencia](persistencia-proyectos.md); los checkpoints
conservan selección, archivo, offset verificado e identidad remota. Recuperar exige evidencia, no sólo existencia.
