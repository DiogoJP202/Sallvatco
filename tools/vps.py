"""Primeira homologação privada. Não é atualizador de produção nem instalador do SO."""
import argparse
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time

ROOT = Path("/srv/sallvat-staging")
HOST = "staging.sallvat.test"
PACKAGE = Path(__file__).resolve().parent


def verify_package(package):
    metadata = json.loads((package / "release.json").read_text(encoding="utf-8"))
    if not re.fullmatch(r"[a-f0-9]{40}", metadata["revision"]):
        raise ValueError("Revisão inválida.")
    if metadata["image_tag"] != "sallvat-vps:" + metadata["revision"]:
        raise ValueError("Tag não corresponde à revisão.")
    if metadata["platform"] != "linux/amd64" or not re.fullmatch(r"sha256:[a-f0-9]{64}", metadata["image_id"]):
        raise ValueError("Imagem/plataforma inválida.")
    required = {"vps.py", "image.tar.gz", "migrations.sql", "deploy/staging/compose.yaml",
                "deploy/staging/init-database.sh", "deploy/staging/start-nginx.sh", "deploy/staging/nginx.conf.template"}
    if not required.issubset(metadata["files"]):
        raise ValueError("Pacote incompleto.")
    if not metadata["migrations"] or not all(re.fullmatch(r"\d{14}_[A-Za-z0-9_]+", m) for m in metadata["migrations"]):
        raise ValueError("Lista de migrations inválida.")
    for relative, expected in metadata["files"].items():
        path = package / relative
        if path.is_symlink() or not path.resolve().is_relative_to(package.resolve()) or not path.is_file():
            raise ValueError("Caminho inválido no pacote.")
        with path.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
                raise ValueError(f"Checksum divergente: {relative}")
    return metadata


def run(*args, data=None):
    # Não permitir que env externo substitua o env-file revisado ou daemon local.
    environment = {k: v for k, v in os.environ.items()
                   if not k.startswith(("SALLVAT_", "STAGING_", "COMPOSE_", "DOCKER_"))}
    result = subprocess.run(args, input=data, capture_output=True, text=True, env=environment)
    if result.returncode:
        # stderr pode conter SQL/configuração. Não reproduzir segredos no terminal.
        raise RuntimeError(f"Falha no comando {args[0]} (código {result.returncode}); verificar diagnóstico privado no host.")
    return result.stdout.strip()


def docker_requirements():
    if sys.platform != "linux" or os.geteuid() != 0:
        raise RuntimeError("Executar com sudo em Linux amd64; não executar na estação Windows.")
    if run("docker", "context", "show") != "default":
        raise RuntimeError("Usar contexto Docker default local.")
    endpoint = json.loads(run("docker", "context", "inspect", "default"))[0]["Endpoints"]["docker"]["Host"]
    if endpoint != "unix:///var/run/docker.sock":
        raise RuntimeError("Docker remoto não é permitido.")
    info = json.loads(run("docker", "info", "--format", "{{json .}}"))
    if info["OSType"] != "linux" or info["Architecture"] not in ("x86_64", "amd64"):
        raise RuntimeError("O kit foi validado somente em Linux amd64.")
    if int(info["ServerVersion"].split(".")[0]) < 28:
        raise RuntimeError("Atualizar Docker Engine para versão suportada >= 28 antes do ensaio.")
    run("docker", "compose", "version")


def compose(*args, data=None):
    return run("docker", "compose", "--project-name", "sallvat-staging",
               "--env-file", str(ROOT / "staging.env"),
               "--file", str(PACKAGE / "deploy/staging/compose.yaml"), *args, data=data)


def settings(metadata):
    files = {"STAGING_APPLICATION_SETTINGS": "application.json", "STAGING_DB_OWNER_PASSWORD_FILE": "owner.txt",
             "STAGING_DB_APP_PASSWORD_FILE": "app.txt", "STAGING_TLS_CERTIFICATE_FILE": "tls.crt",
             "STAGING_TLS_KEY_FILE": "tls.key", "STAGING_ACCESS_PASSWORDS_FILE": "access.htpasswd"}
    return {"SALLVAT_IMAGE": metadata["image_tag"], "STAGING_HOST": HOST,
            **{key: str(ROOT / "secrets" / value) for key, value in files.items()}}


def private_file(path, content, uid=0, mode=0o400):
    with path.open("x", encoding="utf-8") as stream:
        stream.write(content)
    os.chmod(path, mode)
    os.chown(path, uid, uid)


def prepare(metadata):
    if ROOT.exists() or ROOT.is_symlink():
        raise RuntimeError("Diretório já existe; nada será sobrescrito. Não apagar dados para repetir prepare.")
    if run("docker", "volume", "ls", "--quiet", "--filter", "label=com.docker.compose.project=sallvat-staging"):
        raise RuntimeError("Já existem volumes de Staging. Revisar instalação anterior.")
    ROOT.mkdir(mode=0o700)
    directory = ROOT / "secrets"
    directory.mkdir(mode=0o700)
    os.umask(0o077)
    owner, password, access = (secrets.token_urlsafe(36) for _ in range(3))
    private_file(directory / "owner.txt", owner, 70)
    private_file(directory / "app.txt", password, 70)
    connection = f"Host=postgres;Database=sallvat_staging;Username=sallvat_app;Password={password};Timeout=10"
    private_file(directory / "application.json", json.dumps({"ConnectionStrings": {"SallvatDatabase": connection}}), 1654)
    hashed = run("openssl", "passwd", "-apr1", "-stdin", data=access + "\n")
    private_file(directory / "access.htpasswd", "homologacao:" + hashed + "\n", 101)
    private_file(directory / "access-login.txt", "Usuário: homologacao\nSenha: " + access + "\n")
    run("openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "30",
        "-subj", "/CN=" + HOST, "-addext", "subjectAltName=DNS:" + HOST,
        "-keyout", str(directory / "tls.key"), "-out", str(directory / "tls.crt"))
    os.chmod(directory / "tls.key", 0o400)
    os.chown(directory / "tls.key", 101, 101)
    os.chmod(directory / "tls.crt", 0o444)
    private_file(ROOT / "staging.env", "".join(f"{key}={value}\n" for key, value in settings(metadata).items()))
    print("Arquivos criados em /srv/sallvat-staging, sem exibir segredos. Certificado de teste: 30 dias.")


def preflight(metadata):
    if ROOT.is_symlink() or ROOT.stat().st_uid != 0 or ROOT.stat().st_mode & 0o077:
        raise RuntimeError("Diretório privado deve pertencer ao root e ter permissão 700.")
    expected = "".join(f"{key}={value}\n" for key, value in settings(metadata).items())
    if (ROOT / "staging.env").read_text() != expected:
        raise RuntimeError("Configuração alterada ou pacote de outra revisão; requer revisão manual, não atualização automática.")
    for path in (ROOT / "secrets").iterdir():
        if path.is_symlink() or not path.is_file() or (path.name != "tls.crt" and path.stat().st_mode & 0o077):
            raise RuntimeError("Permissões de segredo inválidas.")
    if run("docker", "image", "inspect", "--format", "{{.Id}}", metadata["image_tag"]) != metadata["image_id"]:
        raise RuntimeError("Imagem local não corresponde ao kit validado.")
    run("openssl", "x509", "-checkend", "86400", "-noout", "-in", str(ROOT / "secrets/tls.crt"))
    if shutil.disk_usage(ROOT).free < 5 * 1024**3:
        raise RuntimeError("Manter pelo menos 5 GiB livres para o ensaio.")
    networks = run("docker", "network", "ls", "--format", "{{.ID}}")
    for identifier in networks.splitlines():
        network = json.loads(run("docker", "network", "inspect", identifier))[0]
        if network["Name"].startswith("sallvat-staging_"):
            continue
        for item in network.get("IPAM", {}).get("Config", []) or []:
            subnet = ipaddress.ip_network(item.get("Subnet", "::/128"))
            if subnet.version == 4 and subnet.overlaps(ipaddress.ip_network("172.29.240.0/28")):
                raise RuntimeError("Sub-rede de proxy conflita com uma rede existente; solicitar configuração revisada.")
    compose("config", "--quiet")
    if "nginx" not in compose("ps", "--status", "running", "--services").splitlines():
        for port in (8081, 8443):
            with socket.socket() as probe:
                try:
                    probe.bind(("127.0.0.1", port))
                except OSError as error:
                    raise RuntimeError(f"Porta local {port} ocupada; não parar outro serviço automaticamente.") from error
    print("Pré-verificação aprovada; não comprova backups, domínio, Admin ou integração externa.")


def database(sql):
    return compose("exec", "-T", "postgres", "psql", "-X", "-U", "sallvat_owner", "-d", "sallvat_staging",
                   "-v", "ON_ERROR_STOP=1", "-At", data=sql)


def require_empty_database(count):
    if count != "0":
        raise RuntimeError("Banco não está vazio. Nenhuma migration aplicada; atualização requer backup e plano próprio.")


def initialize(metadata, confirmation):
    if confirmation != metadata["revision"]:
        raise RuntimeError("Confirmar explicitamente a revisão com --confirm-revision.")
    preflight(metadata)
    import fcntl
    with (ROOT / "initialize.lock").open("a") as lock:
        os.chmod(ROOT / "initialize.lock", 0o600)
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if compose("ps", "--status", "running", "--services").strip():
            raise RuntimeError("Primeira instalação exige stack parada; não interromper serviços existentes automaticamente.")
        compose("up", "--detach", "--wait", "--wait-timeout", "90", "postgres")
        count = database("SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")
        require_empty_database(count)
        print("Aplicando migrations explicitamente no banco vazio de homologação...")
        database((PACKAGE / "migrations.sql").read_text(encoding="utf-8-sig"))
        verify_schema(metadata)
        print("Schema inicial aplicado. Aplicação ainda não iniciada; nenhum Admin ou catálogo foi criado.")


def verify_schema(metadata):
    applied = database('SELECT "MigrationId" FROM __ef_migrations_history ORDER BY "MigrationId";').splitlines()
    if applied != metadata["migrations"]:
        raise RuntimeError("Schema difere do pacote; não iniciar web.")


def check(metadata):
    preflight(metadata)
    verify_schema(metadata)
    login = (ROOT / "secrets/access-login.txt").read_text().splitlines()
    password = login[1].removeprefix("Senha: ")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", password):
        raise RuntimeError("Formato de credencial de homologação inválido.")
    for path, expected in (("/conta/entrar", "200"), ("/perfumes", "200"), ("/health/ready", "404")):
        for attempt in range(30):
            try:
                status = run("curl", "--silent", "--show-error", "--noproxy", "*", "--max-time", "5",
                             "--cacert", str(ROOT / "secrets/tls.crt"), "--connect-to", f"{HOST}:443:127.0.0.1:8443",
                             "--config", "-", "--output", "/dev/null", "--write-out", "%{http_code}",
                             "https://" + HOST + path, data=f'user = "homologacao:{password}"\n')
                if status == expected:
                    break
            except RuntimeError:
                pass
            time.sleep(1)
        else:
            raise RuntimeError("Verificação HTTPS falhou: " + path)
    print("HTTPS, acesso privado, catálogo e bloqueio do health externo aprovados.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["verify", "load", "prepare", "preflight", "initialize", "start", "check", "status"])
    parser.add_argument("--confirm-revision")
    args = parser.parse_args()
    metadata = verify_package(PACKAGE)
    if args.action == "verify":
        print("Checksums aprovados: " + metadata["revision"])
        return
    docker_requirements()
    if args.action == "load":
        run("docker", "load", "--input", str(PACKAGE / "image.tar.gz"))
        if run("docker", "image", "inspect", "--format", "{{.Id}}", metadata["image_tag"]) != metadata["image_id"]:
            raise RuntimeError("Imagem importada divergente.")
        print("Imagem importada e ID confirmado.")
    elif args.action == "prepare":
        prepare(metadata)
    elif args.action == "preflight":
        preflight(metadata)
    elif args.action == "initialize":
        initialize(metadata, args.confirm_revision)
    elif args.action == "start":
        preflight(metadata)
        verify_schema(metadata)
        compose("up", "--detach", "--wait", "--wait-timeout", "90")
        print("Stack iniciada somente em loopback. Validar via túnel SSH.")
    elif args.action == "check":
        check(metadata)
    else:
        print(compose("ps"))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        print(f"Interrompido: {error}", file=sys.stderr)
        sys.exit(1)
