## 1. Resolver contract

- [x] 1.1 Add a resolver fixture test that requires the model revision request to carry `blobs=true` and returns a sibling with `lfs.sha256`.
- [x] 1.2 Decode the published SHA-256 into a meaningfully named transient candidate field and update internal consumers away from `LFSOID`/OID terminology.
- [x] 1.3 Add resolver tests proving an absent hash remains distinguishable from a malformed non-empty SHA-256, and make malformed metadata fail with an artifact-specific diagnostic.

## 2. Reconciliation behavior

- [x] 2.1 Update online reconciliation fixtures to use `lfs.sha256` and prove matching local bytes produce a non-partial full sidecar.
- [x] 2.2 Preserve and test refusal without sidecar publication when the local digest disagrees with the published SHA-256.
- [x] 2.3 Preserve and test the explicit partial result only when the matched sibling genuinely publishes no content hash.
- [x] 2.4 Update repository-wide reconciliation tests to prove matching, mismatching, and genuinely hashless siblings remain isolated per artifact.

## 3. Update detection and diagnostics

- [x] 3.1 Update `check-updates` to consume the corrected content SHA-256 and test current, changed-content, changed-revision, and genuinely hashless outcomes.
- [x] 3.2 Replace diagnostics and comments that attribute an unrequested hash or refer to an LFS OID with accurate content-hash terminology.

## 4. Verification and evidence

- [x] 4.1 Run the focused model package tests and the full `task verify` workspace check.
- [x] 4.2 Set `COQUENA_HOME` to the project's test `home/`, repeat reconciliation for `lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF:Q4_0` and the representative repository sample, and record which artifacts still reconcile as partial after the correction; record its known remote weight update as drift evidence without implementing update or upgrade here.
- [x] 4.3 Update the relevant finding/RFC evidence with the verified post-change residue, keeping Xet behavior explicitly unverified unless an identified Xet-backed witness was tested.
