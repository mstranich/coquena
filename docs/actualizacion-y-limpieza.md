# Actualización, verificación y limpieza

## Update y plan

Update consulta novedades compatibles con intención YAML de la combinación elegida
y guarda plan de versión actual → propuesta por recurso. No descarga pesos ni
mueve archivos. Un pin no se convierte en latest. Error, ambigüedad y ausencia de
evidencia no equivalen a «sin cambios».

Conservar intención, resoluciones de partida, destino exacto y archivos necesarios.
Edición YAML posterior invalida el plan: upgrade pide update nuevo, sin recalcular
otro y aplicarlo silenciosamente. No actualizar otras combinaciones ni descubrir
YAML desconocidos por adivinación.

## Upgrade y publicación conjunta

Aplicar el plan persistido sin editar YAML. Sin filtros, conjunto previsto; con
selector, tipo/recurso elegido. Preparar y verificar TODOS los recursos seleccionados
antes de publicar resoluciones. Un fallo conserva la resolución anterior completa,
sin mezcla de shards/versiones ni éxito parcial del conjunto.

Se permite preparar con instancia activa, en ubicaciones separadas sin sobrescribir
sus archivos. Instancia conserva snapshot; lock puede representar siguiente ejecución.
Actualizar no reinicia. Preparado no equivale a última buena acreditada.

Reutilizar contenido solo con correspondencia demostrada. Commit, ETag, puntero Git
y digest de pesos no son intercambiables. Reanudar conserva selección exacta.
Externos no se adoptan ni actualizan automáticamente.
Esto incluye copias introducidas por el usuario dentro del proyecto: su ubicación
o un matching informativo con Hugging Face no las convierte en gestionadas.

## Recuperación protegida

Conservar conjunto anterior hasta comprobar nuevo arranque. Tras health check,
nueva última buena reemplaza a anterior; sus recursos solo pasan a limpieza sin
otras protecciones. Éxito local no se importa como éxito desde otra máquina.
Política de [servidores](servidores.md#recuperación-y-fallos).

## Verificación y bloqueos

Verify comprueba la definición de la receta seleccionada y la existencia/integridad
de los recursos que utiliza en su proyecto. No audita los recursos de su target global.
`verify --global` comprueba las definiciones y recursos del ámbito global; sólo en
ese ámbito admite `--select` para elegir recursos y `--include-unused` para incluir
instalaciones no utilizadas del inventario global. SHA-256 completo por archivo
contra referencia, incluyendo binarios/dependencias gestionados, modelos/shards y
companions. No equivale a autenticidad del publicador.

No descargar, reparar, rebasar hash, modificar YAML ni resoluciones por verify.
Guardar observaciones locales separadas de referencia. Externo sin baseline no
se certifica. Discrepancia bloquea nuevos inicios que la seleccionen en ambos modos,
sin fallback por integridad ni detener instancia activa.

Nuevo verify exitoso o redescarga completa verificada resuelve el fallo concreto.
Promover descarga a explícita no lo repara. Excluir recurso/preset permite otra
composición sin afirmar que se reparó el archivo. Error YAML se reevalúa al reintentar.

## Origen automático o explícito

Motivo de descarga y propiedad gestionado/externo son independientes.

- Automático: obtenido por YAML/actualización; candidato a limpieza sin referencias.
- Explícito: descarga solicitada por CLI; protegido localmente aunque no esté en YAML.
  La descarga no añade intención ni sube archivos al repositorio.
- Pedir explícitamente uno automático ya presente lo protege sin descargar de nuevo.
- Quitar protección por acción explícita no borra; permite limpieza sin otras referencias.

Edición de intención por CLI no equivale a pedir descarga explícita.

## Limpieza

Proteger instancia activa, identidad incierta, combinaciones registradas, recuperación
vigente, operaciones pendientes y descargas explícitas. No imponer retención de cinco
builds que retire esas referencias. Externos conservan archivos.
Una copia local registrada como externa también se conserva: pertenecer a la
carpeta no la convierte en archivo propio eliminable por limpieza.

Mostrar recursos elegibles y espacio recuperable, y pedir confirmación antes de borrar.
Borrar solo inventario propio; conservar vecinos y archivos añadidos por otros.
Retirada parcial mantiene diagnóstico/estado y es reintentable; no afirmar bytes
liberados hasta que se eliminaron.

Olvidar combinación retira sus referencias/resolución/última buena sin borrar YAML
o pesos. Sin advertencia especial de pérdida de recuperación; sí informar espacio
que queda recuperable y comando para limpiarlo. Referencias independientes y activo
permanecen protegidos. Retirar intención no ejecuta borrado espontáneo.

Verbo clean y confirmación no interactiva se definen en [comandos](comandos.md).
No inferir borrado sin confirmación.

## Referencias entre consumidores y pins

El ámbito global y cada proyecto mantienen inventarios independientes. Un servicio
que publica rutas de otro ámbito mantiene referencias cruzadas a sus revisiones
físicas. Limpieza consulta ambas raíces bajo coordinación; un consumidor ausente,
inaccesible o de identidad incierta no autoriza liberar su recurso por inferencia.
Down confirmado retira la referencia de publicación; otras combinaciones, pins,
última buena y descargas explícitas pueden seguir protegiéndolo.

Update informa novedades de un backend fijado sin cambiar el pin. Upgrade de ese
recurso exige intención latest y plan nuevo; no modifica YAML por sí mismo. El
build antes fijado sigue protegido hasta unprotect explícito, que informa impacto
y no elimina otras protecciones. Cambiar variante tampoco autoriza borrar activos.
