# Prepares an isolated Coquena home and two demo projects for the VHS recordings:
#   ai-lab  a 270M model of a few MiB, to show a quick `up` (getting-started).
#   studio  a 12B reasoning model, whose longer answers fill the Live charts (tui-live).
# Usage, from the repository root:  pwsh docs/media/demo/setup.ps1
param([string]$Root = 'C:\demo')

$ErrorActionPreference = 'Stop'

function Write-Recipe([string]$Name, [string]$Yaml) {
    $project = Join-Path $Root $Name
    New-Item -ItemType Directory -Force $project | Out-Null
    $Yaml | Set-Content (Join-Path $project 'coquena.yaml')
    $project
}

$aiLab = Write-Recipe 'ai-lab' @'
name: ai-lab
presets:
  chat:
    args:
      model: unsloth/gemma-3-270m-it-GGUF:UD-IQ2_XXS
      ctx-size: 8192
      temperature: 1.0
      top-p: 0.95
      top-k: 64
      min-p: 0.0
'@

$studio = Write-Recipe 'studio' @'
name: studio
presets:
  chat:
    args:
      model: unsloth/gemma-4-12b-it-GGUF:UD-IQ2_M
      ctx-size: 32768
      temperature: 1.0
      top-p: 0.95
      top-k: 64
      min-p: 0.0
'@

# micro keeps its settings and backups inside the demo folder.
$micro = Join-Path $Root 'micro'
New-Item -ItemType Directory -Force $micro | Out-Null
'{ "backup": false, "savecursor": false, "autoindent": false, "autoclose": false }' | Set-Content (Join-Path $micro 'settings.json')

$env:COQUENA_HOME = Join-Path $Root 'home'
coquena config language set en-US
# Frequent readings, so the Live charts draw several points per request.
coquena config observer-interval set 0.5

Write-Host "Demo ready. Run once per project, interactively, to download and publish:"
foreach ($project in $aiLab, $studio) {
    Write-Host "  `$env:COQUENA_HOME='$env:COQUENA_HOME'; cd $project; coquena up --start-service"
}
