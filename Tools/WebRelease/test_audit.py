import gzip
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import audit


class ReleaseAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root/"index.html").write_text("<title>Block Nations</title>")

    def test_checks_decompressed_game_data_without_disclosing_the_secret(self):
        credential = b"fixture-credential-do-not-display"
        (self.root/"game.data.unityweb").write_bytes(gzip.compress(b"serialized asset " + credential))
        with patch.object(audit, "known_secrets", return_value=[credential]):
            with self.assertRaises(ValueError) as caught: audit.inspect(self.root)
        self.assertIn("Known credential", str(caught.exception))
        self.assertNotIn(credential.decode(), str(caught.exception))

    def test_rejects_api_key_in_utf16_serialized_text(self):
        credential = b"fixture-serialized-credential"
        (self.root/"game.data").write_bytes(credential.decode().encode("utf-16-le"))
        with patch.object(audit, "known_secrets", return_value=[credential]):
            with self.assertRaisesRegex(ValueError, "Known credential"): audit.inspect(self.root)

    def test_rejects_private_files(self):
        (self.root/".env").write_text("placeholder only")
        with self.assertRaisesRegex(ValueError, "Private material"): audit.inspect(self.root)

    def test_rejects_oversized_cloudflare_asset(self):
        with patch.object(audit, "MAX_ASSET", 2):
            with self.assertRaisesRegex(ValueError, "per-file limit"): audit.inspect(self.root)

    def test_accepts_clean_export(self):
        (self.root/"game.data.unityweb").write_bytes(gzip.compress(b"clean game data"))
        result = audit.inspect(self.root)
        self.assertEqual(result["credentialMatches"], 0)
        self.assertEqual(result["files"], 2)


if __name__ == "__main__": unittest.main()
