# Helper de validación: entorno normalizado, logs y códigos comprobados.
function Invoke-CoquenaCheck {
    param([string[]]$Arguments, [int]$Expected = 0)
    $info = [Diagnostics.ProcessStartInfo]::new($taskExecutable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $variables = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $variables[$entry.Key] = $entry.Value }
    $info.Environment.Clear()
    foreach ($key in $variables.Keys) { $info.Environment[$key] = [string]$variables[$key] }
    $info.Environment['COQUENA_HOME'] = $taskHome
    $info.Environment['TEMP'] = $taskTemp
    $info.Environment['TMP'] = $taskTemp
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (!$process.WaitForExit(60000)) {
            $process.Kill($true)
            throw "Tiempo excedido: $($Arguments -join ' ')"
        }
        $result = $stdout.Result + $stderr.Result
        [IO.File]::AppendAllText($taskLog, "coquena $($Arguments -join ' ') => $($process.ExitCode)`n$result`n")
        if ($process.ExitCode -ne $Expected) { throw "Código inesperado: $($Arguments -join ' '): $result" }
        return $result
    }
    finally { $process.Dispose() }
}
