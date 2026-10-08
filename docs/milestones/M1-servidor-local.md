# M1 — Servidor local con dos modelos

**Estado:** borrador.

## Objetivo

Entregar un ejecutable Coquena para Windows x64, C#/.NET 10 y Native AOT, con el que
el usuario pueda instalar un backend oficial, descargar dos modelos GGUF, crear
un preset por modelo y ejecutar un único servidor en Router Mode. Ambos presets
deben ser accesibles para inferencia, con métricas y logs consultables desde Coquena.

Borrador de alcance y tareas: no autoriza implementación ni selección de paquetes.
No es el milestone de todas las funcionalidades de Coquena ni habilita todavía la TUI.

## Referencias canónicas

- [CLI](../comandos.md)
- [Servidores y persistencia](../servidores.md)
- [Modelos y companions](../modelos-y-companions.md)
- [Descargas](../descargas.md)
- [Parámetros](../catalogo-de-parametros.md)
- [Guía del catálogo](../implementacion-catalogo-modelos.md)

Las reglas existentes no se copian ni se reinterpretan. Las entregas M0–M6 de la guía
son piezas del catálogo, no milestones del producto; esta tarjeta cubre un recorrido
vertical que también necesita backend, configuración y servidor.

## Alcance propuesto

- CLI mínima del recorrido, sin comandos ficticios ni salida --json.
- SDK 10.0.401, net10.0 y publicación win-x64 Native AOT.
- Persistencia JSON indentado, transacciones JSONL y logs de llama-server como texto.
- Un backend oficial instalado y seleccionado; default inicial según lo acordado.
- Sólo CPU, CUDA y Vulkan para descubrimiento, selección e instalación de backends
  oficiales. CUDA conserva sus variantes por versión mayor de bibliotecas (por ejemplo
  `cuda@12` y `cuda@13`), independientes del release de llama.cpp. ROCm, SYCL, OpenVINO
  y otras familias quedan fuera de M1, aunque el release oficial publique artefactos.
- Dos modelos gestionados distintos descargados desde Hugging Face público.
- Cada modelo conserva su unidad completa; si la selección tiene shards, se descargan
  y validan todas sus partes. No limitar artificialmente el dominio a un archivo.
- Dos presets explícitos ofrecidos por un servidor, con rutas locales en el INI.
- Host local, puerto registrado único y métricas activas por defecto.
- Start en primer plano o -d, ps con instantánea de métricas, logs/-f, stop y restart.
- Descargas en primer plano, pausa con Ctrl+C, GUID y reanudación conservadora.
- Diagnósticos y recuperación mínima: no declarar instalado algo parcial ni matar
  procesos de identidad incierta.

## Segundo milestone

Update/upgrade, externos/try-match, companions, importación/exportación de parámetros,
clonación y demás funciones iniciales restantes pasan al
[milestone M2](M2-gestion-completa.md).
Los comandos no implementados no se anuncian como disponibles.

TUI, HTTP propio, autenticación HF, fuentes de terceros y multiplataforma ya eran alcance
futuro aplazado antes del recorte. Su inclusión en M2 requiere aclaración; no se asume.
## Tareas LeanSpec

Tarjetas en estado planned, con dependencias registradas en LeanSpec. No autorizan
implementación ni dan por completado el milestone. El orden numérico orienta la lectura;
las dependencias determinan qué puede comenzar, no una secuencia obligatoriamente lineal.

- [ ] [001 — Base ejecutable C# y Native AOT](../../specs/001-m1-base-native-aot/README.md)
- [ ] [002 — Contratos y fixtures mínimos de M1](../../specs/002-m1-contratos-m1/README.md)
- [ ] [003 — Home y persistencia local segura](../../specs/003-m1-persistencia-local/README.md)
- [ ] [004 — Transferencias y comandos de descarga reanudable](../../specs/004-m1-transferencias-reanudables/README.md)
- [ ] [005 — Hardware y selección de backend oficial](../../specs/005-m1-seleccion-backend/README.md)
- [ ] [006 — Instalación y selección del backend oficial](../../specs/006-m1-instalacion-backend/README.md)
- [ ] [007 — Selección Hugging Face y menú de descarga](../../specs/007-m1-seleccion-hugging-face/README.md)
- [ ] [008 — Inspección GGUF y coherencia de shards](../../specs/008-m1-gguf-integridad/README.md)
- [ ] [009 — Descarga y publicación de modelos gestionados](../../specs/009-m1-instalacion-modelos/README.md)
- [ ] [015 — Resolución robusta de unidades GGUF en Hugging Face](../../specs/015-m1-resolucion-gguf-hugging-face/README.md): prioridad inmediata después de 009; corregir el caso Gemma y la dependencia exclusiva de nombres convencionales. Su aceptación bloquea la entrega 014, no las tareas independientes de configuración/servidores.
- [ ] [010 — Configuración, presets y generación de INI](../../specs/010-m1-config-presets-ini/README.md)
- [ ] [011 — Definiciones de servidor, defaults y puertos](../../specs/011-m1-definiciones-server/README.md)
- [ ] [012 — Inicio, parada, reinicio e identidad de procesos](../../specs/012-m1-ciclo-server/README.md)
- [ ] [013 — Instantánea de métricas y consulta de logs](../../specs/013-m1-metricas-logs/README.md)
- [ ] [014 — Recorrido real y entregable Native AOT M1](../../specs/014-m1-entrega-m1/README.md)

La transferencia común se prueba con fixtures antes de integrarla con los proveedores.
Cada tarjeta incluye alcance, aceptación y evidencia Native AOT; los detalles técnicos
se concretan en su entrega sin reabrir reglas funcionales ni introducir un framework.

## Criterios de aceptación

- [ ] El usuario puede repetir el recorrido con el ejecutable publicado, sin usar dotnet run.
- [ ] Instala un backend oficial compatible y descarga dos modelos elegidos explícitamente.
- [ ] Una interrupción de descarga devuelve GUID; resume continúa sin cambiar selección.
- [ ] El servidor ofrece dos presets de modelos distintos y ambos generan una respuesta.
- [ ] Métricas por modelo accesibles y reflejadas en server ps; modelo no cargado o
      consulta fallida tiene diagnóstico, no cifras inventadas ni desaparición de instancia.
- [ ] Logs disponibles en primer plano y por comando; Ctrl+C en logs -f no detiene servidor.
- [ ] -d sobrevive al cierre de terminal; stop/restart y persistencia cumplen las reglas.
- [ ] Un shard faltante, dependencia rota, puerto ocupado o backend ausente no inicia
      silenciosamente un servidor parcial ni selecciona sustitutos.
- [ ] Pruebas automáticas con recursos pequeños y fallos inyectados, más una prueba
      real opt-in y acotada; publicación y ejecución Native AOT verificadas.

## Por cerrar al aprobar el borrador

Elegir backend/build y dos modelos para la demostración según VRAM/RAM disponibles,
definir versiones de dependencias a probar con AOT. Las tareas ya están separadas en tarjetas
con dependencias. La descarga simultánea en disco no exige cargar ambos modelos
en VRAM a la vez: esa gestión corresponde al router del backend.
