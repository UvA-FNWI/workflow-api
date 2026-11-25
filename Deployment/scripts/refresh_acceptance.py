#!/usr/bin/env python3
"""Export or import a Milestones MongoDB archive."""

import argparse
import json
import shutil
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import parse_qs, unquote_plus, urlsplit

EXCLUDED_COLLECTIONS = ("settings",)
EXCLUDED_PREFIXES = ("fs.", "system.")


def main(argv=None):
    script_directory = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("export", "import"))
    parser.add_argument(
        "config",
        nargs="?",
        type=Path,
        help="JSON config. Defaults to prd.json for export and acc.json for import.",
    )
    parser.add_argument(
        "archive",
        nargs="?",
        type=Path,
        help="Archive path. Defaults to DATABASE.archive.gz on export and workflow.archive.gz on import.",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Replace matching collections. Import defaults to a dry run.",
    )
    args = parser.parse_args(argv)

    is_export = args.command == "export"
    if is_export and args.apply:
        parser.error("--apply is for import")

    tool = "mongodump" if is_export else "mongorestore"
    if not shutil.which(tool):
        parser.exit(
            1,
            f"{tool} not found on PATH. Install the MongoDB Database Tools.\n"
            "https://www.mongodb.com/docs/database-tools/installation/\n",
        )

    config_path = args.config or script_directory / ("prd.json" if is_export else "acc.json")
    try:
        database = uri_database(parser, json.loads(config_path.read_text())["uri"])
        default_archive = f"{database}.archive.gz" if is_export else "workflow.archive.gz"
        archive_path = args.archive or script_directory / default_archive
        command = [tool, "--config", str(config_path), "--gzip"]
        if is_export:
            if archive_path.exists() or archive_path.is_symlink():
                parser.error(f"Archive {archive_path} already exists. Choose a new archive file.")
            export_archive(command, archive_path)
        else:
            import_archive(command, database, archive_path, args.apply)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


def uri_database(parser, connection):
    uri = urlsplit(connection)
    options = {key.lower(): values for key, values in parse_qs(uri.query.replace(";", "&")).items()}
    insecure_tls_options = {
        "sslinsecure",
        "tlsinsecure",
        "tlsallowinvalidcertificates",
        "tlsallowinvalidhostnames",
    }

    if (
        uri.scheme != "mongodb"
        or not uri.hostname
        or not uri.username
        or not uri.password
        or uri.fragment
        or options.get("tls") != ["true"]
        or insecure_tls_options.intersection(options)
    ):
        parser.error("Use an authenticated TLS URI")

    database = unquote_plus(uri.path.removeprefix("/"))
    if not 0 < len(database.encode()) < 64 or set(database).intersection('/\\. "$*\0'):
        parser.error("Put a valid MongoDB database name in the URI path")
    return database


def export_archive(command, archive_path):
    command.extend(f"--excludeCollection={name}" for name in EXCLUDED_COLLECTIONS)
    command.extend(f"--excludeCollectionsWithPrefix={prefix}" for prefix in EXCLUDED_PREFIXES)

    with tempfile.TemporaryDirectory(prefix=".refresh-", dir=archive_path.parent) as directory:
        temporary_archive = Path(directory) / "dump.archive.gz"
        command.append(f"--archive={temporary_archive}")
        subprocess.run(command, check=True, umask=0o077)
        # Publish the completed archive without overwriting an existing file.
        archive_path.hardlink_to(temporary_archive)


def import_archive(command, database, archive_path, apply):
    command.extend([
        f"--archive={archive_path}",
        "--nsInclude=*.*",
        "--nsFrom=$db$.$collection$",
        f"--nsTo={database}.$collection$",
        "--stopOnError",
    ])
    if apply:
        command.append("--drop")
    else:
        command.extend(["--dryRun", "--verbose"])

    subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
