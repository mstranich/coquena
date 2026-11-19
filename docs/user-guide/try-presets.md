# Try presets

Trying a preset closes each round of the loop described in
[Getting started](getting-started.md#try-another-configuration): once a preset is
published, you can try it without another client. Chat with it, send it a single
prompt from a script, or load and unload it on the router.

These commands name a preset the same way:

| Form | Example |
| --- | --- |
| Key, inside the project folder | `chat` |
| Published name | `ai-lab/chat` |
| Service, project, and key | `default/ai-lab/chat` |

Without `--service`, Coquena uses the service the recipe publishes to. `--service
<name|default|self>` or `--global` chooses another one. None of these commands publish
presets.

## Chat

```powershell
coquena chat ai-lab/chat
```

This opens a conversation in the terminal. Enter sends a message, and the reply
streams as it arrives. Below the input, a telemetry row shows prompt and generation
speed, cache use, accepted drafts, and the time to the first token. The conversation
lasts for the session; nothing is saved.

| In the chat | Does |
| --- | --- |
| `/reasoning` | Show or hide the model's reasoning, dimmed, before each answer |
| `/exit` or Ctrl+C | Close the chat |

`--reasoning` starts with the reasoning shown. `--load` asks the router to load the
preset when the chat opens; Enter sends once loading finishes. If the service is
stopped, Coquena offers to start it.

The Chat tool of the [terminal interface](terminal-interface.md) does the same, and
the Chat button in the Live tab opens it with the focused preset.

## Send one prompt

`-p` (or `--prompt`) sends one prompt and exits, without an interactive terminal:

```powershell
coquena chat ai-lab/chat -p "Summarize the plot of Hamlet in one sentence."
Get-Content notes.md | coquena chat ai-lab/chat -p -
```

`-` reads the whole prompt from standard input. The answer goes to standard output as
it arrives, so you can pipe it into another tool. The finish reason and the metrics go
to standard error. `--reasoning` also writes the reasoning before the answer.

`--json` writes one object when the reply ends, with the service, the preset, the
answer, the reasoning, the finish reason, any problem, and the metrics:

```powershell
coquena chat ai-lab/chat -p "Hello" --json
```

| Exit code | Meaning |
| --- | --- |
| 0 | The reply finished. |
| 1 | The request failed. |
| 2 | The preset or the service is not available. |
| 130 | Cancelled with Ctrl+C. |

A single prompt never starts a stopped service. Add `--load` to wait for the preset
to load before sending.

## Load and unload

A router loads a preset on its first request. To load it in advance, or to free its
memory, run:

```powershell
coquena load ai-lab/chat
coquena unload ai-lab/chat
```

Both wait for the final state: up to 120 seconds to load and 60 to unload. If the preset
is already in that state, nothing is sent. Unloading a preset that is serving requests
asks for confirmation, so it needs an interactive terminal.

In the Live tab, the focused preset offers the same Load and Unload actions, plus
Withdraw.
