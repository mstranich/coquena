param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$SourceHome,
    [string]$ValidationRoot = 'D:\Coquena-task035-validation', [int]$Port = 18085)
$ErrorActionPreference = 'Stop'
$taskExecutable = (Resolve-Path -LiteralPath $Executable).Path
$taskSource = (Resolve-Path -LiteralPath $SourceHome).Path
if (![IO.Path]::IsPathFullyQualified($ValidationRoot) -or [IO.Path]::GetPathRoot($ValidationRoot) -eq 'C:\') { throw 'Usar una raíz absoluta de validación fuera de C:' }
$taskHome = Join-Path $ValidationRoot ('real-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $taskHome
$taskLog = Join-Path $taskHome 'cli.txt'
$taskTemp = Join-Path $ValidationRoot 'temp'
$null = New-Item -ItemType Directory -Path $taskTemp -Force
. (Join-Path $PSScriptRoot 'invoke-cli.ps1')
$sourceBackends = Join-Path $taskSource 'catalogs/backends.json'
$sourceModels = Join-Path $taskSource 'catalogs/models.json'
$originalCatalogHashes = @((Get-FileHash $sourceBackends).Hash, (Get-FileHash $sourceModels).Hash)
$backend = (Get-Content $sourceBackends -Raw | ConvertFrom-Json).backends | Where-Object { $_.variant -eq 'cuda@13' -and $_.build -eq 'b11482' } | Select-Object -First 1
if (!$backend) { throw 'Falta cuda@13 b11482 acordado' }
function Resolve-InstalledPath($Record, [string]$Path, [bool]$External) {
    if ($External) { return $Path }
    $base = $taskSource
    if ($Record.installationDirectory) { $base = [IO.Path]::GetFullPath($Record.installationDirectory, $taskSource) }
    return Join-Path $base $Path
}
$backendPath = Resolve-InstalledPath $backend $backend.serverPath ($backend.ownership -eq 'external')
$models = (Get-Content $sourceModels -Raw | ConvertFrom-Json).models
$names = @('google/gemma-4-26B-A4B-it-qat-q4_0-gguf:Q4_0','prism-ml/Ternary-Bonsai-27B-gguf:Q2_0')
$taskFiles = @()
try {
    $null = Invoke-CoquenaCheck -Arguments @('backend','register',$backendPath,'--name','m2-real-runtime','--family','cuda@13')
    $null = Invoke-CoquenaCheck -Arguments @('backend','use','m2-real-runtime')
    for ($i = 0; $i -lt $names.Count; $i++) {
        $model = $models | Where-Object name -EQ $names[$i]
        if (!$model -or $model.files.Count -ne 1) { throw "Recurso acordado ausente o dividido: $($names[$i])" }
        $path = Resolve-InstalledPath $model $model.files[0].localPath ($model.ownership -eq 'external')
        $file = Get-Item -LiteralPath $path
        $taskFiles += @{ Path = $path; Length = $file.Length; LastWrite = $file.LastWriteTimeUtc }
        $null = Invoke-CoquenaCheck -Arguments @('model','add',$path,'--name',$names[$i])
        $preset = @('m2-gemma','m2-bonsai')[$i]
        $null = Invoke-CoquenaCheck -Arguments @('preset','create',$names[$i],$preset)
        $null = Invoke-CoquenaCheck -Arguments @('preset','set',$preset,'ctx-size','2048')
        $layers = @('20','all')[$i]
        $null = Invoke-CoquenaCheck -Arguments @('preset','set',$preset,'n-gpu-layers',$layers)
    }
    $null = Invoke-CoquenaCheck -Arguments @('server','create','m2-real','--port',"$Port")
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','use','m2-real-runtime')
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','set','models-max','1')
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','preset','add','m2-gemma','m2-bonsai')
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','show')
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','start','-d')
    $uri = "http://127.0.0.1:$Port"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        try { $null = Invoke-RestMethod "$uri/v1/models" -TimeoutSec 2; break }
        catch { if ([DateTime]::UtcNow -gt $deadline) { throw }; Start-Sleep -Milliseconds 500 }
    } while ($true)
    foreach ($preset in @('m2-bonsai','m2-gemma')) {
        $body = @{ model = $preset; messages = @(@{role='user'; content='Reply with a short greeting.'}); max_tokens=256; temperature=0 } | ConvertTo-Json -Depth 5
        $response = Invoke-RestMethod "$uri/v1/chat/completions" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 300
        $response | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $taskHome ($preset + '-response.json'))
        if ([string]::IsNullOrWhiteSpace($response.choices[0].message.content)) { throw "Respuesta final vacía: $preset" }
        $null = Invoke-CoquenaCheck -Arguments @('server','ps')
        Write-Output "$preset respondió: $($response.choices[0].message.content)"
    }
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','logs')
    $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','restart')
    $null = Invoke-CoquenaCheck -Arguments @('server','ps')
    Write-Output "OK: inferencia de ambos presets, alternancia y restart; evidencia: $taskHome"
}
finally {
    if (Test-Path (Join-Path $taskHome 'catalogs/servers.json')) {
        $servers = (Get-Content (Join-Path $taskHome 'catalogs/servers.json') -Raw | ConvertFrom-Json).servers
        if ($servers | Where-Object name -EQ 'm2-real') {
            $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','stop')
            $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','logs')
            $null = Invoke-CoquenaCheck -Arguments @('server','m2-real','remove')
            if (!(Get-ChildItem (Join-Path $taskHome 'logs/m2-real') -Filter '*.log')) { throw 'La retirada perdió los logs reales' }
        }
    }
    if ((Get-FileHash $sourceBackends).Hash -ne $originalCatalogHashes[0] -or (Get-FileHash $sourceModels).Hash -ne $originalCatalogHashes[1]) { throw 'Catálogos de origen cambiaron durante el recorrido' }
    foreach ($entry in $taskFiles) {
        $file = Get-Item -LiteralPath $entry.Path
        if ($file.Length -ne $entry.Length -or $file.LastWriteTimeUtc -ne $entry.LastWrite) { throw "Archivo de origen cambió: $($entry.Path)" }
    }
}
