# Shared look and hidden environment for every demo. Included with: Source docs/media/demo/common.tape
Set Shell "pwsh"
Set Padding 24
Set WindowBar Colorful
Set TypingSpeed 45ms
Set Theme "Builtin Dark"

Hide
Type "$demo = Join-Path $PWD 'docs\media\demo'; $env:COQUENA_HOME='C:\demo\home'; $env:COLORTERM='truecolor'; Remove-Item env:NO_COLOR -ErrorAction Ignore; $env:MICRO_CONFIG_HOME='C:\demo\micro'; function prompt { '> ' }; Set-PSReadLineOption -PredictionSource None -HistorySaveStyle SaveNothing; cd C:\demo\ai-lab; cls"
Enter
Sleep 1s
Show
