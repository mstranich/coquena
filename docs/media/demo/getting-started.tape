# docs/user-guide/getting-started.md as a GIF: write the recipe, start it, list the
# models, chat, then one round of the loop (another quantization, up, load, chat).
Output "docs/media/demo/out/getting-started.gif"
Set FontSize 22
Set Width 1280
Set Height 720
Source "docs/media/demo/common.tape"
Set TypingSpeed 25ms

# Start from an empty project and a stopped service.
Hide
Type "coquena down *> $null; coquena service default stop *> $null; Remove-Item coquena.yaml -ErrorAction Ignore; cls"
Enter
Sleep 4s
Show

# Write a recipe.
Type "micro coquena.yaml"
Enter
Sleep 1s
Type "name: ai-lab"
Enter
Type "presets:"
Enter
Type "  chat:"
Enter
Type "    args:"
Enter
Type "      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M"
Enter
Type "      ctx-size: 4096"
Sleep 1.5s
Ctrl+S
Sleep 300ms
Ctrl+Q
Sleep 300ms

# Start. The first time it downloads the model: wait for the prompt instead of a fixed time.
Type "coquena up --start-service"
Enter
Wait@300s />\s*$/
Sleep 1.5s
Screenshot "docs/media/demo/out/check-gs-up.png"

# The service is an OpenAI-compatible server.
Type "(Invoke-RestMethod http://127.0.0.1:9931/v1/models).data.id"
Enter
Sleep 2s

# Try it.
Type "cls; coquena chat ai-lab/chat"
Enter
Sleep 2s
Type "Hello! Who are you?"
Enter
Sleep 8s
Screenshot "docs/media/demo/out/check-gs-chat.png"
Type "/exit"
Enter
Sleep 1s

# Try another configuration: another quantization and a larger context.
Type "cls; micro coquena.yaml"
Enter
Sleep 1s
Ctrl+A
Type "name: ai-lab"
Enter
Type "presets:"
Enter
Type "  chat:"
Enter
Type "    args:"
Enter
Type "      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q8_0"
Enter
Type "      ctx-size: 8192"
Sleep 1.5s
Ctrl+S
Sleep 300ms
Ctrl+Q
Sleep 300ms
Type "coquena up"
Enter
Wait@300s />\s*$/
Type "coquena load ai-lab/chat"
Enter
Wait@120s />\s*$/
Type `coquena chat ai-lab/chat -p "Hello! Who are you?"`
Enter
Wait@60s />\s*$/
Sleep 3s
Screenshot "docs/media/demo/out/check-gs-loop.png"

# Restore the demo: original recipe, presets published again.
Hide
Type "& (Join-Path $demo 'setup.ps1') *> $null; coquena up --start-service"
Enter
Sleep 10s
