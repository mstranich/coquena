param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Runner,
    [string]$ValidationRoot = 'D:\Coquena-task035-validation')
$ErrorActionPreference = 'Stop'
$taskExecutable = (Resolve-Path -LiteralPath $Executable).Path
$taskRunner = (Resolve-Path -LiteralPath $Runner).Path
if (![IO.Path]::IsPathFullyQualified($ValidationRoot) -or [IO.Path]::GetPathRoot($ValidationRoot) -eq 'C:\') { throw 'Usar una raíz absoluta de validación fuera de C:' }
$taskRoot = Join-Path $ValidationRoot ('integrated-' + [guid]::NewGuid().ToString('N'))
$taskHome = Join-Path $taskRoot 'home'
$null = New-Item -ItemType Directory -Path $taskRoot
$taskLog = Join-Path $taskRoot 'cli.txt'
$taskTemp = Join-Path $ValidationRoot 'temp'
$null = New-Item -ItemType Directory -Path $taskTemp -Force
. (Join-Path $PSScriptRoot 'invoke-cli.ps1')
function Check([bool]$Condition, [string]$Description) { if (!$Condition) { throw $Description } }
# Cabecera GGUF mínima. No son pesos ni evidencia de inferencia.
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint32]0x46554747); $writer.Write([uint32]3)
$writer.Write([uint64]1); $writer.Write([uint64]1)
foreach ($value in @('general.architecture', 'test')) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    $writer.Write([uint64]$bytes.Length); $writer.Write($bytes)
    if ($value -eq 'general.architecture') { $writer.Write([uint32]8) }
}
$modelPath = Join-Path $taskRoot 'principal.gguf'
$companionPath = Join-Path $taskRoot 'companion.gguf'
[IO.File]::WriteAllBytes($modelPath, $stream.ToArray())
[IO.File]::WriteAllBytes($companionPath, $stream.ToArray())
$writer.Dispose(); $stream.Dispose()
$beforeHash = (Get-FileHash $modelPath).Hash
try {
    $null = Invoke-CoquenaCheck -Arguments @('--help')
    $null = Invoke-CoquenaCheck -Arguments @('config','param-reset','--dry-run')
    Check (!(Test-Path $taskHome)) 'Dry-run inicial creó el home'
    $null = Invoke-CoquenaCheck -Arguments @('backend','register',$taskRunner,'--name','fixture-runtime','--family','cpu')
    $backend = (Get-Content (Join-Path $taskHome 'catalogs/backends.json') -Raw | ConvertFrom-Json).backends[0]
    $null = Invoke-CoquenaCheck -Arguments @('backend','use','fixture-runtime')
    $null = Invoke-CoquenaCheck -Arguments @('model','add',$modelPath,'--name','fixture/principal:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('model','companion','add',$companionPath,'--name','fixture/companion#mmproj:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('model','verify','fixture/principal:BF16','--set-baseline')
    $null = Invoke-CoquenaCheck -Arguments @('model','verify','fixture/principal:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('model','companion','refresh','fixture/companion#mmproj:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('preset','create','fixture/principal:BF16','original')
    $null = Invoke-CoquenaCheck -Arguments @('preset','bind','original','mmproj','fixture/companion#mmproj:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('preset','clone','original','clon')
    $null = Invoke-CoquenaCheck -Arguments @('preset','set','original','ctx-size','1024')
    $presetSnapshots = (Get-Content (Join-Path $taskHome 'catalogs/presets.json') -Raw | ConvertFrom-Json).presets
    $original = $presetSnapshots | Where-Object name -EQ 'original'
    $clone = $presetSnapshots | Where-Object name -EQ 'clon'
    Check ($original.id -ne $clone.id -and $original.bindings.mmproj -eq $clone.bindings.mmproj -and !$clone.parameters.'ctx-size') 'Clon no independiente o vínculo perdido'
    $null = Invoke-CoquenaCheck -Arguments @('param','define','m2-audit','--type','string','--scope','model','--target','ini')
    $null = Invoke-CoquenaCheck -Arguments @('preset','set','clon','m2-audit','value')
    $presetFile = Join-Path $taskHome 'catalogs/presets.json'
    $beforeReset = (Get-FileHash $presetFile).Hash
    $null = Invoke-CoquenaCheck -Arguments @('config','param-reset','--dry-run')
    Check ((Get-FileHash $presetFile).Hash -eq $beforeReset) 'Dry-run cambió presets'
    $null = Invoke-CoquenaCheck -Arguments @('model','update')
    $null = Invoke-CoquenaCheck -Arguments @('model','upgrade')
    $null = Invoke-CoquenaCheck -Arguments @('backend','update')
    $null = Invoke-CoquenaCheck -Arguments @('backend','upgrade')
    $null = Invoke-CoquenaCheck -Arguments @('server','default','preset','add','clon')
    # Puerto asignado por el SO, distinto del default persistido.
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $null = Invoke-CoquenaCheck -Arguments @('server','default','set','port',"$port")
    $null = Invoke-CoquenaCheck -Arguments @('server','default','start','-d')
    $null = Invoke-CoquenaCheck -Arguments @('server','default','remove') -Expected 1
    $null = Invoke-CoquenaCheck -Arguments @('model','companion','remove','fixture/companion#mmproj:BF16') -Expected 1
    $null = Invoke-CoquenaCheck -Arguments @('backend','uninstall', $backend.id) -Expected 1
    $null = Invoke-CoquenaCheck -Arguments @('server','default','stop')
    $null = Invoke-CoquenaCheck -Arguments @('model','companion','remove','fixture/companion#mmproj:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('server','default','start','-d') -Expected 1
    $null = Invoke-CoquenaCheck -Arguments @('model','remove','fixture/principal:BF16')
    $null = Invoke-CoquenaCheck -Arguments @('backend','uninstall',$backend.id)
    $null = Invoke-CoquenaCheck -Arguments @('server','default','remove')
    Check ((Get-FileHash $modelPath).Hash -eq $beforeHash -and (Test-Path $companionPath) -and (Test-Path $taskRunner)) 'Se alteraron archivos externos'
    Write-Output "OK: recorrido integrado CLI, externos preservados, home/log: $taskRoot"
}
finally {
    if (Test-Path (Join-Path $taskHome 'catalogs/servers.json')) {
        $servers = (Get-Content (Join-Path $taskHome 'catalogs/servers.json') -Raw | ConvertFrom-Json).servers
        if ($servers | Where-Object name -EQ 'default') { $null = Invoke-CoquenaCheck -Arguments @('server','default','stop') }
    }
}
