# Social media, ~25 s: live charts of a preset in the TUI while requests run.
# Send load from outside VHS, from the repository root, before starting vhs:
#   Remove-Item docs/media/demo/out/tui-ready -ErrorAction Ignore
#   0..2 | % { Start-Job -FilePath docs/media/demo/load.ps1 -ArgumentList 40 }
# The jobs wait for the tui-ready marker, written just before the TUI opens, so the
# prompt phase of the first requests happens while the charts are on screen.
Output "docs/media/demo/out/tui-live.gif"
Output "docs/media/demo/out/tui-live.mp4"
Set FontSize 18
Set Width 1600
Set Height 1000
Source "docs/media/demo/common.tape"

# Hidden marker: the load jobs start a few seconds after it, once the TUI shows the preset.
Hide
Type "New-Item -Force (Join-Path $demo 'out\tui-ready') *> $null; cls"
Enter
Sleep 300ms
Show

Type "coquena tui"
Enter
Sleep 3s

# Enter on the Live tab focuses the tree. Up/Down move the selection; Right would leave
# the tree for the service buttons, so it is not used.
Enter
Up
Sleep 23s
