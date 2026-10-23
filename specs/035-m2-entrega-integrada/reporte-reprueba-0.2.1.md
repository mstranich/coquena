# Reporte para revisión después de la reprueba de 0.2.1

Las referencias Git de Coquena de este documento apuntan a sus equivalentes en el historial saneado. Los hashes de modelos y backends externos se conservan.

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

17 de octubre de 2026. Solicitado por el usuario durante la prueba; complementa
la [evidencia del recorrido](reprueba-0.2.1.md). Código revisado: `fb1fc6b`.
No se implementaron correcciones ni se crearon tarjetas nuevas. Los puntos
siguientes requieren evaluación; no constituyen una política nueva del producto.

## 1. El companion a4lg MTP-ONLY no puede volver a descargarse como MTP

**Observado.** Después de retirar correctamente el companion instalado en la
prueba anterior, `model companion download
a4lg/Qwen3.6-35B-A3B-MTP-ONLY-GGUF#mtp:Q8_0` informa que la cuantización no es
seleccionable y ofrece únicamente README/cancelación. No crea una operación.
El inventario remoto conserva `Qwen3.6-35B-A3B-MTP-ONLY-Q8_0.gguf`, de
1 990 649 632 bytes. La receta previa no puede reconstruirse por el selector
actual, aunque ese companion ya había funcionado en inferencia.

**Implementación actual y fundamento.**
[ModelSelection.NameRole](../../src/Coquena/Models/ModelResolution.cs) reconoce
marcadores delimitados `mtpdraft`/`mtp-draft`, carpeta exacta `mtp` y prefijo
`mtp-` del basename. El nombre de a4lg contiene `MTP-ONLY` internamente y queda
clasificado como principal; la variante resultante no tiene tipo companion MTP.
No es un error del tipo numérico ni falta del archivo remoto: el filtro por rol
lo excluye. Esto deriva de la política explícitamente aprobada en 037 para evitar
confundir un principal con MTP integrado con un draft separado, pero omite una
convención real utilizada en el recorrido anterior.

**Impacto comprobado.** Bloquea descargar de nuevo ese recurso mediante el comando
de companions. No afecta la instalación de los dos principales Ornith ni la
selección del draft EryriLabs `mtpdraft-Q8_0.gguf`, que sí pudo descargarse,
vincularse y ejecutar con el principal oficial. Esa alternativa permite probar
el runtime, pero no resuelve el caso a4lg ni demuestra equivalencia de rendimiento.

**Decisión pendiente y orientación.** El usuario indicó que debe corregirse para
contemplar el caso y pidió revisar este reporte después de las pruebas. Evaluar
`MTP-ONLY` como marcador explícito de candidato auxiliar, con límites de token
claros, sin volver a aceptar cualquier token interno `MTP`. Conservar el estado
candidato, las contradicciones de metadatos y la ausencia de garantía de
compatibilidad; ni `general.type=model` ni el tamaño certifican el rol separado.
La corrección deberá cubrir el inventario a4lg real fijado y la regresión de los
principales Ornith/BigBang con MTP en su nombre. La regla canónica de marcadores
necesitará reflejar esta ampliación expresamente; no adaptar documentación al
código de forma implícita.

**Horizonte.** Una futura preparación declarativa M3 encontraría la misma exclusión
si consume el clasificador compartido. Es una consecuencia del mecanismo actual,
no evidencia de un fallo de una función M3 todavía no implementada. La corrección
de clasificación no debe convertirse en certificación de aceleración o arquitectura.

**Evidencia.** `${CoquenaHome}/validation/0.2.1-retest/download-mtp-interactive.txt`,
`download-2.txt`, `mtp-tree.json`, `sources/mtp.md`; antecedente de instalación
y ejecución en [prueba-ornith.md](prueba-ornith.md). El catálogo nuevo contiene
EryriLabs, no a4lg; esta diferencia queda explícita para el uso posterior.

## 2. La instalación inicial CUDA 12 falló al publicar; resume la recuperó

**Observado.** `backend install cuda@12 --release latest` resolvió b11712 y
transfirió sus dos ZIP completos, 658 584 979 bytes en total. El intento terminó
`failed/publishing` con `transfer-io-failed`; aún no había un backend CUDA 12
publicado. `download resume 00b1a758-fdb5-4511-a654-4a1e3d793482` completó
instalación y limpieza en el mismo GUID sin nueva descarga. El backend recuperado
pasó la carga real e inferencia con los tres presets.

**Implementación actual y fundamento.**
[DownloadManager](../../src/Coquena/Downloads/DownloadManager.cs) convierte las
excepciones de E/S/red no tipadas en ese diagnóstico fijo. El comentario del código
explica que evita exponer credenciales o URLs efímeras. En esta fase,
[BackendInstaller](../../src/Coquena/Backends/BackendInstaller.cs) vuelve a comprobar
el ejecutable preparado, publica su directorio e índice y luego limpia. El estado
conservado permite recuperar la preparación; el resultado real confirma esa
recuperación. El diagnóstico guardado no identifica qué operación de E/S falló.

**Impacto comprobado y límite.** Requirió una intervención manual en una instalación
nueva. No se observó pérdida de bytes ni un backend incompleto presentado como
instalado. No está demostrada la causa: bloqueo del ejecutable, contención,
antivirus u otra E/S son hipótesis. No debe atribuirse automáticamente a 036.
La prueba 0.2.0 también registró una instalación CUDA con un síntoma de E/S cuya
causa no se estableció; no se afirma que ambos incidentes compartan causa.

**Qué evaluar.** Localizar primero la operación y el error concreto en una
reproducción controlada, manteniendo la preparación y el plan originales.
Considerar diagnósticos seguros de etapa/tipo/código de sistema que permitan
distinguir causas sin registrar secretos. No justificar reintentos genéricos,
menos exclusión o validación relajada solamente porque resume funcionó.
En M3 o una interfaz operativa, la intervención manual y un diagnóstico demasiado
general podrían dificultar la recuperación guiada; ese impacto futuro es un riesgo,
no una funcionalidad evaluada aquí.

**Evidencia.** `backend-failed-operation.json`, `commands.txt`, `publish.txt`,
`llama-help.txt` y `final-server.log` bajo
`${CoquenaHome}/validation/0.2.1-retest`. Nuevo backend
`efa33533-1878-4489-bd9d-1dc6bd00f2af`, b11712, commit `10b8b7553`.

## Resultados resueltos por las reglas y validación

La retirada explícita liberó los pesos propios; las referencias rotas se conservaron
y bloquearon el inicio, sin sustitución silenciosa. La cascada de definición retiró
el binding sin borrar el companion, y el reset retiró asignaciones añadidas conservando
las compatibles. Cancelar la primera cascada mantuvo los hashes de los catálogos.
Los tres externos quedaron desregistrados con cero bytes liberados y hashes de sus
archivos intactos. Los recursos vecinos y la configuración global se conservaron.
Estos son comportamientos previstos, no hallazgos nuevos.

Validación ejecutada: publicación Native AOT win-x64; limpieza y reconstrucción por
CLI; pausa/reanudación del 9B; publicación de modelos mientras otras transferencias
seguían activas; inferencia, código generado y tool calls en los tres presets;
alternancia por router; métricas y rechazo de retiradas activas. Los resultados
detallados y límites están en la evidencia enlazada. No se llenó el contexto completo,
no se certificó un óptimo de rendimiento ni se conectó un harness agéntico real.

## Estado de revisión y lanzamiento

El entorno quedó utilizable con los tres presets y el companion EryriLabs como
alternativa de prueba. La reconstrucción exacta del caso a4lg sigue pendiente de la
corrección solicitada; además, hubo una recuperación manual del backend. Por tanto,
el recorrido no se presenta como una pasada limpia que habilite automáticamente
0.2.1. Versión sin modificar; sin commit, tag, push ni release de esta reprueba.
Se entrega el reporte para la revisión posterior pedida por el usuario.

## Seguimiento posterior

El usuario solicitó convertir este reporte en una única tarea e implementarla.
Se creó [040: MTP-ONLY y publicación CUDA](../040-m2-reprueba-mtp-publicacion-cuda/README.md),
que conserva ambos hallazgos y registra su implementación, pruebas y límites.
Los resultados anteriores describen la reprueba original, no el estado posterior
de esa corrección. No se reabrieron 036–039 ni se crearon tareas adicionales.
