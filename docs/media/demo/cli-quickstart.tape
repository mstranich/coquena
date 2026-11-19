# Social media, ~12 s: from a five-line YAML to a published preset.
Output "docs/media/demo/out/cli-quickstart.mp4"
Output "docs/media/demo/out/cli-quickstart.gif"
Set FontSize 22
Set Width 1280
Set Height 720
Source "docs/media/demo/common.tape"

Type "micro coquena.yaml"
Sleep 300ms
Enter
Sleep 2.5s
Screenshot "docs/media/demo/out/check-cli-1.png"
Ctrl+Q
Sleep 500ms

Type "coquena up"
Sleep 300ms
Enter
Sleep 5s
Screenshot "docs/media/demo/out/check-cli-2.png"

Type "coquena ps"
Sleep 300ms
Enter
Sleep 3s
Screenshot "docs/media/demo/out/check-cli-3.png"
