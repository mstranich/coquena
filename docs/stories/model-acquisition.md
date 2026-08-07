# Story: getting a model onto the machine and keeping it

**Kind:** story · **State:** current · **Owner:** project-owner

## Who

The first user is the project owner, running Coquena on their own workstation.
The next users are technically literate people who already run local models —
the kind who have LM Studio installed, know what a quantization is, and are
tired of managing GGUF files by hand.

They are not trying to learn a new abstraction. They are trying to get a model
running without a folder full of scripts.

## The story

> I want to download an LLM onto my PC.
>
> I open a terminal and use Coquena to **discover** which quantizations are
> available to download, and therefore which files will actually be fetched — so
> I know how many GB this is going to cost me.
>
> Knowing what I am about to download, I ask Coquena for **the quantization**.
> Coquena downloads one or more files, but it must be a complete bundle so I get
> the most out of it.
>
> I do not want to think about which revision or hash I am downloading. I work
> with logical names that point at, and group, the files that correspond to a
> concrete filename and hash inside Hugging Face.
>
> Once it is downloaded I can:
>
> - bind a quantization to a preset;
> - check for updates to a quantization or its companions;
> - download those updates;
> - delete a quantization or a companion.
>
> Separately, I can **import** files: GGUFs already downloaded by other tools
> like LM Studio, or fetched with a download manager, or with plain `curl` or
> `wget`. That is what makes reconciling useful.
>
> And I may have my own fine-tuned models, for specific purposes — the kind a
> company keeps for one internal domain.

## What the story commits Coquena to

- **The unit the operator names is a quantization, not a file.** A quantization
  may be several files. Coquena is responsible for keeping that set complete.
- **Revisions are Coquena's problem, not the operator's.** The operator names
  things logically; Coquena resolves that to concrete bytes and remembers which
  ones.
- **Cost is visible before the download, not after.** Discovery that does not
  tell you the size has not done its job.
- **Locally obtained files are first-class.** Importing is not a fallback for a
  failed download; it is how a large share of real libraries came to exist.
- **A model may have no remote at all.** A private fine-tune is a legitimate
  library member and cannot be treated as an artifact awaiting reconciliation
  with a repository that does not exist.

## What this story does not cover

Serving, presets beyond the act of binding one, llama.cpp runtime management,
and the web UI. Those are separate stories and are deliberately absent here so
this one stays checkable.

## Why it is written down

Every command, field and backlog entry should be traceable to a step above. A
command that serves no step is a candidate for deletion, not for a
justification written after the fact.

This story is expected to change. People change and so do their needs; when it
does, it is superseded rather than edited into something nobody agreed to, and
the artifacts that referenced it keep resolving.
