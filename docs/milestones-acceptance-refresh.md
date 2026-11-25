# Refresh the Milestones acceptance database

Run from the repo root on macOS or Linux with Python 3.10+ and the
[MongoDB Database Tools](https://www.mongodb.com/docs/database-tools/installation/) on your `PATH`.

## Set up once

In `Deployment/scripts`, copy `prd.json.example` to `prd.json` and
`acc.json.example` to `acc.json`.

Replace the URI placeholders in each copy with that environment's connection
details. Percent-encode the username and password.

The database name goes in the URI path. Import restores the collections
from a single-database archive made by `export` into that database.

## Export and preview

```sh
python3 Deployment/scripts/refresh_acceptance.py export &&
python3 Deployment/scripts/refresh_acceptance.py import
```

Export names the archive after the source database. To import a different
archive, pass its path as shown under **Back up acceptance**.

## Apply

Run this in the same terminal after reviewing the dry run.

```sh
python3 Deployment/scripts/refresh_acceptance.py import --apply
```

Apply replaces the matching collections in acceptance. Acceptance-only
collections stay in place. Export skips `settings`, `fs.*`, and `system.*`.

## Back up acceptance

To save a copy of acceptance before a refresh, export with `acc.json` and a
separate archive filename.

```sh
python3 Deployment/scripts/refresh_acceptance.py export Deployment/scripts/acc.json Deployment/scripts/acceptance-backup.archive.gz
```

To preview restoring the backup, pass its archive path to `import`.

```sh
python3 Deployment/scripts/refresh_acceptance.py import Deployment/scripts/acc.json Deployment/scripts/acceptance-backup.archive.gz
```

Add `--apply` to that command after reviewing the preview.
