# M3 — Definiciones declarativas de servidores

**Estado:** borrador. **Depende de:** [M2](M2-gestion-completa.md).

## Objetivo

Distribuir definiciones de ejecución mediante YAML y catálogos de parámetros
personalizados mediante JSON, evitando scripts lanzadores escritos a mano.
Las reglas y los pendientes se mantienen en
[Definiciones declarativas](../definiciones-declarativas.md).

## Entregas

- Leer un YAML con uno o varios servidores, presets por modelo y parámetros.
- Preparar modelos y builds oficiales faltantes e iniciar los servidores definidos.
- Referenciar explícitamente ejecutables externos y resolver rutas relativas al YAML.
- Importar y exportar definiciones de parámetros en JSON.
- Validar estrictamente por documento, con sobrescritura por servidor.
- Conservar el estado de ejecución y detener servidores por nombre.

## Límites y preparación

No incorpora repositorios de terceros como proveedores, adaptadores de runtime,
backup completo del manager ni soporte automático de interfaces de servidor distintas.
La antigua tarjeta exploratoria 036 se retira: sus casos de intercambio se concretan
en estas dos clases de archivos. Las tarjetas de M3 se definirán al cerrar los
pendientes canónicos; este milestone no autoriza implementación.

## Aceptación

- Definición compartible sin rutas absolutas obligatorias ni scripts auxiliares.
- Errores estrictos diagnosticados antes de descargas e inicio.
- Recursos externos preservados y ejecución identificable por nombre.
- Pruebas funcionales y publicación/ejecución Native AOT para el destino acordado.
