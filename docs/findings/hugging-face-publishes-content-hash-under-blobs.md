# Hugging Face publishes the content hash, but only if you ask for it

**Kind:** finding · **State:** current · **Verified:** 2026-08-05

## The fact

The model revision endpoint returns a **minimal** sibling list by default. Blob
detail requires `?blobs=true`, and the hash is published under **`lfs.sha256`**.

Verified against `lmstudio-community/gemma-4-12B-it-QAT-GGUF` at revision
`aaec3dd9d1012557147a627142759994d1fd8d37`:

```text
GET /api/models/{repo}/revision/{rev}              → zero occurrences of "lfs"
GET /api/models/{repo}/revision/{rev}?blobs=true   → the block below
```

```json
"rfilename": "gemma-4-12B-it-QAT-Q4_0.gguf",
"blobId": "01e97ac6aa548d894f07a1b77725603c1d6fae40",
"size": 6975879008,
"lfs": {
  "sha256": "f568ac5de71c8fcac5d5794494388ad94db9e18b4368ca897e21b30d2448eeec",
  "size": 6975879008,
  "pointerSize": 135
}
```

The whole response contains **zero occurrences of `"oid"`**. `lfs.sha256` is the
SHA-256 of the file contents and is directly comparable to a locally computed
digest. The reported size matched the local file exactly.

## Coverage

With `?blobs=true` and redirects followed, every repository in the operator's
library publishes it:

```text
ornith-ai/Ornith-1.0-9B-GGUF   7    lmstudio-community/gemma-4-12B-it-QAT-GGUF      2
ornith-ai/Ornith-1.0-35B-GGUF  7    lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF  2
Qwen3.6-35B-A3B-GGUF           4    prism-ml/Bonsai-27B-gguf                        6
prism-ml/Ternary-Bonsai-27B-gguf                                                    8
```

No repository in this sample required weaker evidence.

**Xet verified 2026-08-07.** With the corrected client,
`lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF:Q4_0` resolved its bytes through
an `xet-bridge-us` URL while the revision metadata still published
`lfs.sha256`. Online reconciliation compared the locally cached file against
that digest and completed non-partially at revision
`0100791afcfc646ad7243c5443d53870e63f0976`; an immediate update check reported
it up to date. Xet transport therefore does not, by itself, remove the content
hash from the model revision response.

The same corrected client reported the installed Ornith 9B, Ornith 35B, Qwen
3.6 35B and Gemma 4 12B witnesses up to date rather than partial. No post-fix
partial residue was observed in this installed sample.

## Why it matters

A client that omits the parameter, or reads a differently named field, will
report that *the service* published no hash. That misattribution is expensive:
it turns a one-line client defect into an apparent property of the ecosystem,
and any design that then asks "what weaker evidence do we accept?" is answering
a question that does not exist.

The lesson generalises: before designing for missing evidence, confirm the
evidence is missing rather than unrequested.

## Consequences

- Byte-level attestation of a locally adopted file against a remote revision is
  achievable today, for the common case.
- Artifacts Coquena downloaded itself are attested by construction — their bytes
  came from a revision URL — so only adopted files depend on this.
