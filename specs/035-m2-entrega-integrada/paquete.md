# Coquena — preparación local M2 para Windows x64

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

Ejecutable Native AOT: no requiere instalar .NET. Extraer todos los archivos del ZIP
y abrir PowerShell en esa carpeta. Los pesos y llama.cpp se obtienen por separado.
Este paquete está preparado para revisión; no acredita una publicación de release.

```powershell
.\coquena.exe --help
.\coquena.exe system probe
```

El home inicial es `%USERPROFILE%\.coquena`. Para usar el directorio elegido en D:
sin modificar variables persistentes:

```powershell
$env:COQUENA_HOME = "${CoquenaHome}"
.\coquena.exe backend list
.\coquena.exe model list
.\coquena.exe model companion list
.\coquena.exe server list
```

Revisar nombres y recursos existentes antes de crear presets o servidores. Para una
instalación nueva, `backend install` elige una familia oficial compatible; `model download
usuario/repositorio` ofrece un menú para elegir pesos. Cada companion se solicita en
otra invocación. Las transferencias son en primer plano: Ctrl+C pausa y muestra el GUID
para `download resume <guid>`; `download cancel <guid>` descarta la preparación.

```powershell
.\coquena.exe backend install
.\coquena.exe model download usuario/repositorio
# Sustituir por el nombre exacto mostrado por model list:
.\coquena.exe preset create usuario/repositorio:cuantizacion conversacion
.\coquena.exe preset set conversacion ctx-size 2048
.\coquena.exe server default preset add conversacion
.\coquena.exe server default start -d
.\coquena.exe server ps
.\coquena.exe server default logs
.\coquena.exe server default stop
```

El servidor inicial escucha en `127.0.0.1:9931`; los servidores ya guardados conservan
su puerto. Consultar `server show` antes de iniciar. La compatibilidad de carga y las
necesidades de RAM/VRAM dependen del backend, modelo y configuración.

`backend update` y `model update` consultan y guardan candidatos sin instalar;
`upgrade` aplica ese resultado fijado. Los externos se registran mediante `backend
register` y `model add`, sin copiar ni adoptar sus archivos. Las retiradas de externos
conservan sus archivos. Las referencias rotas requieren reparación explícita.

`config models-dir` y `config backends-dir` admiten rutas relativas al home y ofrecen
traslado; leer su informe antes de confirmar una fusión. `config param-reset --dry-run`
consulta impacto sin escrituras; aplicar el reset exige confirmación y puede eliminar
asignaciones incompatibles.

La validación integrada comprende fixtures de publicaciones, fallos y recuperación,
además del recorrido real limitado documentado en el checkout. No acredita otras
plataformas, fallo físico de discos, modelos multimodales, contexto grande o calidad
de inferencia. El paquete no incluye TUI, HTTP propio, autenticación Hugging Face,
proveedores de descarga de terceros ni ejecución declarativa YAML.

La licencia de Coquena se incluye en `LICENSE`. Backends y modelos conservan sus
licencias propias.
