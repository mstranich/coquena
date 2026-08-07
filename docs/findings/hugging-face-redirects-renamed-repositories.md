# Repositories get renamed and the API answers with a redirect

**Kind:** finding · **State:** current · **Verified:** 2026-08-05

## The fact

A Hugging Face repository can be renamed or moved. The old path keeps working:
the API answers **HTTP 307 Temporary Redirect** to the new one.

Observed in the operator's own library:

```text
deepreinforce-ai/Ornith-1.0-9B-GGUF   → ornith-ai/Ornith-1.0-9B-GGUF
deepreinforce-ai/Ornith-1.0-35B-GGUF  → ornith-ai/Ornith-1.0-35B-GGUF
```

Both artifacts were downloaded under the old name and their sidecars still
record it.

## Why it matters

Because the redirect is followed transparently by any ordinary HTTP client,
**nothing appears to be wrong**. Downloads work, revisions resolve, update
checks compare successfully. The divergence is silent.

But the recorded repository is no longer the canonical one, and no field
distinguishes "the name I asked for" from "the name that answered". This is an
identity problem, not a transport one: an attestation binds bytes to a revision
*of a repository*, so if the repository identity can drift unnoticed, the
attested revision inherits that ambiguity.

A rename is also invisible to an update check that compares revisions. A rename
does not rewrite git history, so the recorded revision still matches the current
one and the check reports "up to date" while the repository has moved.

## Consequences

- Update detection needs a notion of change that is not only "the revision
  advanced".
- The sidecar may need to distinguish the requested identity from the resolved
  one, the same way it already separates `RequestedRevision` from
  `ResolvedRevision`.
- Deletion, re-download and reporting all key off a repository name that may be
  stale.

## Confidence

The redirect and the rename are verified. That an update check reports "up to
date" across a rename is inferred from how revisions are compared and has
**not** been reproduced end to end.
