# Social media, ~12 s: chat with a local model inside the terminal.
Output "docs/media/demo/out/tui-chat.mp4"
Set FontSize 22
Set Width 1280
Set Height 720
Source "docs/media/demo/common.tape"

Type "coquena tui"
Sleep 300ms
Enter
Sleep 2.5s

# Tools tab, then the message box.
Up Up Up
Right Right
Enter
Sleep 1.5s
Tab
Screenshot "docs/media/demo/out/check-chat-1.png"
Type "Write a haiku about running AI on my own GPU"
Sleep 400ms
Enter
Sleep 5s
Screenshot "docs/media/demo/out/check-chat-2.png"

Escape
Sleep 500ms
