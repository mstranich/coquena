# User guide cover, ~20 s: Live, Definitions and Tools.
Output "docs/media/demo/out/tui-tour.gif"
Output "docs/media/demo/out/tui-tour.mp4"
Set FontSize 22
Set Width 1280
Set Height 720
Source "docs/media/demo/common.tape"

Type "coquena tui"
Sleep 300ms
Enter
Sleep 3s

# Live: select the service, then the published preset.
Up
Sleep 1.5s
Screenshot "docs/media/demo/out/check-tour-1.png"
Right
Sleep 300ms
Down
Sleep 2s
Screenshot "docs/media/demo/out/check-tour-2.png"

# Definitions.
Escape
Up Up Up
Right
Enter
Sleep 3s
Screenshot "docs/media/demo/out/check-tour-3.png"

# Tools › Chat.
Up Up Up
Right
Enter
Sleep 3s
Screenshot "docs/media/demo/out/check-tour-4.png"

Escape
Sleep 500ms
