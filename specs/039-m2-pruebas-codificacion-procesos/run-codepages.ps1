param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Runner,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$TempDirectory = 'D:\coquena\validation\039\temp',
    [string]$Harness = "$PSScriptRoot/../035-m2-entrega-integrada/run-native-tests.ps1")
$ErrorActionPreference = 'Stop'
$taskConfig = @{
    Executable = (Resolve-Path -LiteralPath $Executable).Path
    Runner = (Resolve-Path -LiteralPath $Runner).Path
    Harness = (Resolve-Path -LiteralPath $Harness).Path
    OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    TempDirectory = [IO.Path]::GetFullPath($TempDirectory)
}
$null = New-Item -ItemType Directory -Path $taskConfig.OutputDirectory -Force
$taskConfigPath = Join-Path $taskConfig.OutputDirectory 'config.json'
$taskConfig | ConvertTo-Json | Set-Content -LiteralPath $taskConfigPath -Encoding utf8
$taskFailed = $false
foreach ($taskPage in 437, 65001) {
    # A separate hidden console prevents chcp from changing the caller's console.
    $taskCommand = @'
$ErrorActionPreference = 'Stop'
$config = Get-Content -Raw -LiteralPath '__CONFIG__' | ConvertFrom-Json
$root = Join-Path $config.OutputDirectory '__PAGE__'
$null = New-Item -ItemType Directory -Path $root -Force
try {
    $page = & "$env:SystemRoot/System32/chcp.com" __PAGE__
    if ($LASTEXITCODE -ne 0) { throw 'chcp failed' }
    $page | Set-Content (Join-Path $root 'codepage.txt') -Encoding utf8
    & $config.Harness -Executable $config.Executable -Runner $config.Runner `
        -Filter '--runtime-tests' -OutputDirectory $root -TempDirectory $config.TempDirectory
    exit 0
} catch {
    $_ | Out-String | Set-Content (Join-Path $root 'failure.txt') -Encoding utf8
    exit 1
}
'@
    $taskCommand = $taskCommand.Replace('__CONFIG__', $taskConfigPath.Replace("'", "''")).Replace('__PAGE__', [string]$taskPage)
    $taskEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($taskCommand))
    $taskChild = Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -WindowStyle Hidden `
        -ArgumentList @('-NoProfile', '-EncodedCommand', $taskEncoded) -PassThru
    try {
        if (-not $taskChild.WaitForExit(180000)) { $taskChild.Kill($true); throw 'Codepage validation timeout' }
        Write-Output "Page ${taskPage}: exit $($taskChild.ExitCode)"
        if ($taskChild.ExitCode -ne 0) { $taskFailed = $true }
    } finally { $taskChild.Dispose() }
}
if ($taskFailed) { throw 'Codepage validation failed; inspect per-page evidence.' }
