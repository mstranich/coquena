param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Runner,
    [string]$Filter = '', [string]$OutputDirectory = 'artifacts/task035-checks',
    [string]$TempDirectory = 'D:\Coquena-task035-validation\temp')
$ErrorActionPreference = 'Stop'
$taskExe = (Resolve-Path -LiteralPath $Executable).Path
$taskRunner = (Resolve-Path -LiteralPath $Runner).Path
$null = New-Item -ItemType Directory -Path $OutputDirectory, $TempDirectory -Force
$taskOutputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskTemp = (Resolve-Path -LiteralPath $TempDirectory).Path
if ([IO.Path]::GetPathRoot($taskTemp) -eq 'C:\') { throw 'Los temporales de esta validación no deben usar C:' }
$taskInfo = [Diagnostics.ProcessStartInfo]::new($taskRunner)
$taskInfo.UseShellExecute = $false
$taskInfo.CreateNoWindow = $true
$taskInfo.RedirectStandardOutput = $true
$taskInfo.RedirectStandardError = $true
$taskInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
$taskInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
$taskVariables = @{}
foreach ($taskEntry in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $taskVariables[$taskEntry.Key] = $taskEntry.Value }
$taskInfo.Environment.Clear()
foreach ($taskKey in $taskVariables.Keys) { $taskInfo.Environment[$taskKey] = [string]$taskVariables[$taskKey] }
$taskInfo.Environment['TEMP'] = $taskTemp
$taskInfo.Environment['TMP'] = $taskTemp
if ($Filter) { $taskInfo.ArgumentList.Add($Filter) }
$taskInfo.ArgumentList.Add($taskExe)
$taskProcess = [Diagnostics.Process]::Start($taskInfo)
try {
    $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
    $taskError = $taskProcess.StandardError.ReadToEndAsync()
    $taskProcess.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $taskOutputRoot 'native-tests.txt'), $taskOutput.Result)
    [IO.File]::WriteAllText((Join-Path $taskOutputRoot 'native-errors.txt'), $taskError.Result)
    Write-Output ('Suite nativa: exit ' + $taskProcess.ExitCode)
    if ($taskProcess.ExitCode -ne 0) { throw $taskError.Result }
}
finally { $taskProcess.Dispose() }
