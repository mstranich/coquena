param([string]$Filter = '', [string]$OutputName = 'native', [string]$RunnerDirectory = 'task029-tests-native')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$taskInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $taskRoot ('artifacts/' + $RunnerDirectory + '/Coquena.Tests.exe')))
$taskInfo.UseShellExecute = $false
$taskInfo.CreateNoWindow = $true
$taskInfo.RedirectStandardOutput = $true
$taskInfo.RedirectStandardError = $true
# Normalize the inherited Path/PATH duplicate before launching a native fixture.
$taskVariables = @{}
foreach ($taskEntry in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $taskVariables[$taskEntry.Key] = $taskEntry.Value }
$taskInfo.Environment.Clear()
foreach ($taskKey in $taskVariables.Keys) { $taskInfo.Environment[$taskKey] = [string]$taskVariables[$taskKey] }
if ($Filter) { $taskInfo.ArgumentList.Add($Filter) }
$taskInfo.ArgumentList.Add((Join-Path $taskRoot 'artifacts/task029-native/coquena.exe'))
$taskProcess = [Diagnostics.Process]::Start($taskInfo)
$taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
$taskError = $taskProcess.StandardError.ReadToEndAsync()
$taskProcess.WaitForExit()
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ($OutputName + '-tests.txt')), $taskOutput.Result)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ($OutputName + '-errors.txt')), $taskError.Result)
Write-Output ($OutputName + ': exit ' + $taskProcess.ExitCode)
exit $taskProcess.ExitCode
