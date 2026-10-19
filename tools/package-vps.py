"""Empacota somente artefatos aprovados; executado no runner Linux, sem secrets."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys


def command(*args):
    return subprocess.check_output(args, text=True).strip()


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    image, sql, output = sys.argv[1:]
    target = Path(output)
    target.mkdir(parents=True, exist_ok=False)
    revision = command("git", "rev-parse", "HEAD")
    tag = f"sallvat-vps:{revision}"
    subprocess.run(["docker", "tag", image, tag], check=True)
    for source in Path("deploy/staging").iterdir():
        if source.name in {"compose.yaml", "init-database.sh", "nginx.conf.template", "start-nginx.sh"}:
            destination = target / "deploy/staging" / source.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
    shutil.copyfile("tools/vps.py", target / "vps.py")
    shutil.copyfile("docs/VPS.md", target / "VPS.md")
    shutil.copyfile(sql, target / "migrations.sql")
    with gzip.open(target / "image.tar.gz", "wb", compresslevel=1) as archive:
        process = subprocess.Popen(["docker", "save", tag], stdout=subprocess.PIPE)
        shutil.copyfileobj(process.stdout, archive)
        process.stdout.close()
        if process.wait() != 0:
            raise RuntimeError("Falha ao exportar imagem.")
    metadata = {
        "revision": revision,
        "image_tag": tag,
        "image_id": command("docker", "image", "inspect", "--format", "{{.Id}}", tag),
        "platform": "linux/amd64",
        "app_uid": 1654,
        "nginx_uid": 101,
        "postgres_uid": 70,
        "migrations": sorted(p.stem for p in Path("src/Sallvat.Infrastructure/Persistence/Migrations").glob("[0-9]*.cs") if not p.name.endswith(".Designer.cs")),
        "files": {str(p.relative_to(target)).replace("\\", "/"): digest(p) for p in sorted(target.rglob("*")) if p.is_file()},
    }
    (target / "release.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(f"Kit Linux amd64 preparado para {revision}; nenhuma credencial incluída.")


if __name__ == "__main__":
    main()
