# Still captures for docs/user-guide. Run from the repository root.
# Each Screenshot names the guide page that would use it.
# TUI keys: focus starts on the tabs; Right/Left choose a tab and Enter opens it, moving the
# focus to the tree. Tab enters the content, Shift+Tab goes back. Esc on a tab quits.
Output "docs/media/demo/out/guide-screens.gif"
Set FontSize 22
Set Width 1280
Set Height 720
Source "docs/media/demo/common.tape"

# getting-started.md: publish and check.
Type "coquena up"
Enter
Sleep 5s
Screenshot "docs/user-guide/images/getting-started-up.png"
Type "cls; coquena ps"
Enter
Sleep 3s
Screenshot "docs/user-guide/images/getting-started-ps.png"

# recipes.md: the recipe in an editor.
Type "cls; micro coquena.yaml"
Enter
Sleep 2s
Screenshot "docs/user-guide/images/recipes-yaml.png"
Ctrl+Q
Sleep 500ms

# Background load, so Live and Tools show activity.
Hide
Type "$load = Start-Job -FilePath (Join-Path $demo 'load.ps1'); cls"
Enter
Sleep 3s
Show

Type "coquena tui"
Enter
Sleep 4s
Screenshot "docs/user-guide/images/tui-live.png"

# services-and-backends.md and try-presets.md: Enter on the Live tab focuses the tree.
Enter
Up
Sleep 2s
Screenshot "docs/user-guide/images/services-live-service.png"
Down 2
Sleep 4s
Screenshot "docs/user-guide/images/try-presets-live-preset.png"

# Definitions: global-and-local.md and models-and-presets.md. Up from the top of the tree reaches the tabs.
Up 3
Right
Enter
Sleep 3s
Screenshot "docs/user-guide/images/global-and-local-definitions.png"
Down 3
Sleep 2s
Screenshot "docs/user-guide/images/models-and-presets-models.png"
Down 2
Sleep 2s
Screenshot "docs/user-guide/images/models-and-presets-presets.png"

# try-presets.md: Tools › Chat; opening it focuses the message box.
Up 8
Right
Enter
Sleep 2s
Type "Say hi in five words"
Enter
Sleep 5s
Screenshot "docs/user-guide/images/try-presets-chat.png"

# configuration.md: ≡ Configuration.
Up
Screenshot "docs/media/demo/out/check-up1.png"
Up
Screenshot "docs/media/demo/out/check-up2.png"
Right 3
Enter
Sleep 2s
Screenshot "docs/user-guide/images/configuration-tui.png"

Ctrl+C
Sleep 500ms
Hide
Type "Stop-Job $load; Remove-Job $load -Force"
Enter