$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runRoot = Join-Path $PSScriptRoot ('doc-validation-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $runRoot
$utf8 = [System.Text.UTF8Encoding]::new($false)
$diagramCount = 0
$jsonCount = 0
$brokenLinks = [System.Collections.Generic.List[string]]::new()

foreach ($doc in Get-ChildItem (Join-Path $repoRoot 'docs') -Recurse -Filter '*.md') {
    $content = Get-Content -LiteralPath $doc.FullName -Raw
    foreach ($match in [regex]::Matches($content, '(?ms)^```plantuml\s*\r?\n(.*?)^```')) {
        $diagramCount++
        $diagramPath = Join-Path $runRoot ('diagram-' + $diagramCount + '.puml')
        [System.IO.File]::WriteAllText($diagramPath, $match.Groups[1].Value, $utf8)
    }
    foreach ($match in [regex]::Matches($content, '(?ms)^```json\s*\r?\n(.*?)^```')) {
        $null = $match.Groups[1].Value | ConvertFrom-Json -ErrorAction Stop
        $jsonCount++
    }
    foreach ($match in [regex]::Matches($content, '\[[^\]]*\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value.Split('#')[0]
        if ($target -and $target -notmatch '^[a-z]+:' -and
            -not (Test-Path -LiteralPath (Join-Path $doc.DirectoryName $target))) {
            $brokenLinks.Add($doc.Name + ': ' + $target)
        }
    }
}

if ($brokenLinks.Count) { throw ($brokenLinks -join "`n") }
$diagrams = @(Get-ChildItem -LiteralPath $runRoot -Filter '*.puml' | ForEach-Object FullName)
& java -jar 'C:\ProgramData\chocolatey\lib\plantuml\tools\plantuml.jar' -checkonly @diagrams
if ($LASTEXITCODE -ne 0) { throw 'PlantUML validation failed' }

$summary = "OK: $diagramCount diagramas PlantUML, $jsonCount ejemplos JSON y enlaces locales de docs."
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'docs.txt'), $summary + "`n", $utf8)
Write-Output $summary
