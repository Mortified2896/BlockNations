import gzip
from pathlib import Path
import tempfile
import struct
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

    def unity_container(self, strings, version=39):
        indices, position = [0], 0
        for value in strings:
            position += len(value); indices.append(position)
        records = struct.pack("<" + str(len(indices)) + "I", *indices)
        literal_start = 32 + len(records)
        metadata = struct.pack("<8I", 0xFAB11BAF, version, 32, len(records), len(indices), literal_start,
                               position, len(strings)) + records + b"".join(strings)
        name = b"Il2CppData/Metadata/global-metadata.dat"
        end = 20 + 12 + len(name)
        return b"UnityWebData1.0\0" + struct.pack("<I", end) + struct.pack("<III", end, len(metadata), len(name)) + name + metadata

    def test_adjacent_culture_and_identifier_literals_are_not_a_key(self):
        (self.root/"game.data.unityweb").write_bytes(gzip.compress(self.unity_container([b"sk-SK", b"slice" * 8])))
        self.assertEqual(audit.inspect(self.root)["credentialMatches"], 0)

    def test_real_key_in_a_managed_literal_is_still_rejected(self):
        (self.root/"game.data").write_bytes(self.unity_container([b"sk-SK", b"sk-proj-" + b"A" * 64]))
        with self.assertRaisesRegex(ValueError, "Credential signature"): audit.inspect(self.root)

    def test_key_outside_managed_literals_is_still_rejected(self):
        (self.root/"game.data").write_bytes(self.unity_container([b"sk-SK", b"slice" * 8]) + b"sk-proj-" + b"A" * 64)
        with self.assertRaisesRegex(ValueError, "Credential signature"): audit.inspect(self.root)

    def test_unknown_metadata_keeps_the_raw_scan(self):
        (self.root/"game.data").write_bytes(self.unity_container([b"sk-SK", b"slice" * 8], version=40))
        with self.assertRaisesRegex(ValueError, "Credential signature"): audit.inspect(self.root)

    def test_known_secret_is_compared_even_across_literal_boundaries(self):
        credential = b"sk-" + b"A" * 64
        (self.root/"game.data").write_bytes(self.unity_container([credential[:30], credential[30:]]))
        with patch.object(audit, "known_secrets", return_value=[credential]):
            with self.assertRaisesRegex(ValueError, "Known credential"): audit.inspect(self.root)


if __name__ == "__main__": unittest.main()
