# Maintenance

These commands keep backends, models, and companions healthy and up to date. They all
work the same way in both [scopes](global-and-local.md): by default they use the
current project, and `--global` selects the global scope. Each project is maintained
separately.

## Update and upgrade

`latest` is resolved once. After that, Coquena keeps using the same build or model
revision, so your setup does not change by surprise, even when you run `up` again.
Moving to something newer takes two explicit steps, as with `apt` on Debian:

```powershell
coquena update
coquena upgrade
```

`update` only looks. It checks for newer versions of the backend, models, and
companions in the current recipe and saves a plan. It downloads nothing and changes
nothing else. Its output is JSON.

`upgrade` applies that plan:

| What | Impact |
| --- | --- |
| Downloads | New files are downloaded and verified next to the current ones. |
| Failures | Valid proposals are applied together. If one fails, they all keep their current version. |
| Blocked resources | Resources that cannot be upgraded are reported and left as they are, without stopping the others. |
| Your recipe | It is not edited. |
| Running services | They keep running with their current files. Restart the service to use the new version. |
| Disk space | Old and new files coexist. The previous set is kept for recovery until a new start succeeds; after that, `coquena clean` can remove it. |

Some things to keep in mind:

- **Editing the recipe invalidates the plan.** Run `update` again before `upgrade`.
- **A fixed version stays fixed.** A pin such as `version: b12100` is never upgraded. To
  move on, change the recipe and run `coquena up`, which prepares what the recipe asks for.
- **External files are not updated.** This covers your own executables and local models.

Add `--global` for the global scope, and `--select` to limit the operation to one
resource. Each project has its own plan, so update them one at a time.

```powershell
coquena update --global
coquena upgrade --global
coquena update --select model://main
coquena upgrade --select model://main
```

## Verify

`verify` audits the recipe and its resources. It never downloads or repairs anything.
See also [Check a recipe](recipes.md#check-a-recipe).

```powershell
coquena verify
coquena verify --global
coquena verify --checksum
```

The default check is quick. `--checksum` also compares each file's SHA-256, which can
take a while with large models.

## Disk space

Models and backends use most of the space, and each scope stores its own. To move
them to a larger drive, change the global folders:

```powershell
coquena config models-dir set D:\coquena\models
coquena config backends-dir set D:\coquena\backends
```

Coquena asks whether to move the existing files. See
[Storage folders](configuration.md#storage-folders).

Coquena never deletes files on its own. Withdrawing presets, removing a recipe, or
uninstalling the tool leaves them in place. To free space, preview what can go first:

```powershell
coquena clean --dry-run
coquena clean --dry-run --global
```

Then remove it. Without `--yes`, `clean` asks for confirmation.

```powershell
coquena clean
coquena clean --global
```

`clean` removes only files Coquena owns. It keeps:

- Resources used by a running service or a registered combination.
- Resources you downloaded explicitly or protected.
- Resources that are part of a pending operation or needed for recovery.
- External files you supplied.

If an old combination is holding resources, run `coquena combination list` and then
`coquena combination forget <id>`. This does not delete files; run `clean` afterwards.
