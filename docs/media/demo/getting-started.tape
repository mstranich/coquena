# docs/user-guide/getting-started.md as a GIF: write the recipe, start, request, change, clean up.
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
Type "      model: unsloth/gemma-3-270m-it-GGUF:UD-IQ2_XXS"
Enter
Type "      ctx-size: 4096"
Sleep 1.5s
Ctrl+S
Sleep 300ms
Ctrl+Q
Sleep 300ms

# Start.
Type "coquena up --start-service"
Enter
# The first time it downloads the model: wait for the prompt instead of a fixed time.
Wait@180s />\s*$/
Sleep 1.5s
Screenshot "docs/media/demo/out/check-gs-up.png"
Type "coquena ps"
Enter
Sleep 3s

# Send a request.
Type `(irm http://127.0.0.1:9931/v1/chat/completions -Method Post -ContentType application/json -Body '{"model":"ai-lab/chat","messages":[{"role":"user","content":"Hello! Who are you?"}],"max_tokens":32,"temperature":0}').choices.message.content`
Enter
Sleep 6s
Screenshot "docs/media/demo/out/check-gs-request.png"

# Change a setting.
Type "coquena preset set chat ctx-size 8192"
Enter
Sleep 2s
Type "coquena up"
Enter
Sleep 5s
Screenshot "docs/media/demo/out/check-gs-change.png"

# Clean up.
Type "cls; coquena down"
Enter
Sleep 4s
Type "coquena service default stop"
Enter
Sleep 4s
Screenshot "docs/media/demo/out/check-gs-clean.png"
Sleep 1s

# Restore the demo: original recipe, presets published again.
Hide
Type "& (Join-Path $demo 'setup.ps1') *> $null; coquena up --start-service"
Enter
Sleep 10s
