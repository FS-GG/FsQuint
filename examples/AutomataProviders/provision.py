#!/usr/bin/env python3
"""Build pinned PostgreSQL extensions in a new private prefix, without sudo or a shared service."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

PINS = {
    "postgres": ("https://github.com/postgres/postgres.git", "3638289fb57bdabec00deda98ee9624a35f5d66a"),
    "pgmq": ("https://github.com/pgmq/pgmq.git", "51d7655a097d91bf05ad5ae40dd604aa067ed355"),
    "pg_cron": ("https://github.com/citusdata/pg_cron.git", "5cedfa472ccc83567aa23ec645925ed8489a7797"),
    "automata": ("https://github.com/byzantine-systems/automata.git", "03c3282f25888a32d36e53fa708f0342c328ccfc"),
}


def run(args, cwd):
    subprocess.run([str(x) for x in args], cwd=cwd, check=True, timeout=900)


def clone(name, destination):
    url, commit = PINS[name]
    destination.mkdir()
    run(["git", "init", "-q"], destination)
    run(["git", "fetch", "--depth", "1", url, commit], destination)
    run(["git", "checkout", "--detach", "FETCH_HEAD"], destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path, help="must not exist; owns tools/ and automata/")
    args = parser.parse_args()
    root = args.destination.resolve()
    root.mkdir(parents=True, exist_ok=False)
    prefix = root / "tools"
    with tempfile.TemporaryDirectory(prefix="fsquint-provider-build-") as temporary:
        temporary = Path(temporary)
        pg = temporary / "postgres"
        clone("postgres", pg)
        run(["./configure", f"--prefix={prefix}", "--without-readline", "--without-icu", "--without-zlib"], pg)
        run(["make", "-j4"], pg)
        run(["make", "install"], pg)
        run(["make", "-C", "contrib/btree_gist", "install"], pg)
        for name, subdir in (("pgmq", "pgmq-extension"), ("pg_cron", ".")):
            path = temporary / name
            clone(name, path)
            option = f"PG_CONFIG={prefix / 'bin/pg_config'}"
            run(["make", option], path / subdir)
            run(["make", "install", option], path / subdir)
    clone("automata", root / "automata")
    (root / "pins.json").write_text(json.dumps(PINS, indent=2) + "\n")


if __name__ == "__main__":
    main()
