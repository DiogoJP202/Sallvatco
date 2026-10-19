import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("vps", Path(__file__).parents[1] / "vps.py")
vps = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(vps)


class VpsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        names = ["vps.py", "image.tar.gz", "migrations.sql", "deploy/staging/compose.yaml",
                 "deploy/staging/init-database.sh", "deploy/staging/start-nginx.sh", "deploy/staging/nginx.conf.template"]
        files = {}
        for name in names:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
            files[name] = hashlib.sha256(b"fixture").hexdigest()
        self.metadata = {"revision": "a" * 40, "image_tag": "sallvat-vps:" + "a" * 40,
                         "image_id": "sha256:" + "b" * 64, "platform": "linux/amd64",
                         "migrations": ["20260930183351_AddPaymentRefundDispatch"], "files": files}
        self.save()

    def save(self):
        (self.root / "release.json").write_text(json.dumps(self.metadata), encoding="utf-8")

    def test_complete_package_passes_without_docker(self):
        self.assertEqual(vps.verify_package(self.root)["revision"], "a" * 40)

    def test_modified_file_is_rejected(self):
        (self.root / "migrations.sql").write_text("alterado")
        with self.assertRaisesRegex(ValueError, "Checksum"):
            vps.verify_package(self.root)

    def test_incomplete_package_is_rejected(self):
        del self.metadata["files"]["image.tar.gz"]
        self.save()
        with self.assertRaisesRegex(ValueError, "incompleto"):
            vps.verify_package(self.root)

    def test_path_escape_is_rejected(self):
        self.metadata["files"]["../escape"] = "a" * 64
        self.save()
        with self.assertRaisesRegex(ValueError, "Caminho"):
            vps.verify_package(self.root)

    def test_mismatched_revision_is_rejected(self):
        self.metadata["image_tag"] = "sallvat-vps:latest"
        self.save()
        with self.assertRaisesRegex(ValueError, "Tag"):
            vps.verify_package(self.root)

    def test_existing_directory_is_never_overwritten(self):
        with patch.object(vps, "ROOT", self.root):
            with self.assertRaisesRegex(RuntimeError, "já existe"):
                vps.prepare(self.metadata)

    def test_initialize_requires_explicit_revision_before_any_command(self):
        with patch.object(vps, "run") as command:
            with self.assertRaisesRegex(RuntimeError, "Confirmar"):
                vps.initialize(self.metadata, None)
            command.assert_not_called()

    def test_nonempty_database_is_not_migrated(self):
        vps.require_empty_database("0")
        for count in ("1", "13", "", "invalid"):
            with self.assertRaisesRegex(RuntimeError, "não está vazio"):
                vps.require_empty_database(count)


if __name__ == "__main__":
    unittest.main()
