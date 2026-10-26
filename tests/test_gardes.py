"""Garde-fous (règle du 25/10, après un essai qui a touché le vrai dossier des plugins) : les outils qui agissent sur
l'environnement réel de l'utilisateur (profil d'Options+, réglages des IA, vrai pad) refusent sans --reel, avant toute écriture.
python -I -m unittest discover -s tests"""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

OUTILS = Path(__file__).resolve().parents[1] / "outils"


class Gardes(unittest.TestCase):
    def lancer(self, script, *arguments, env=None):
        return subprocess.run([sys.executable, "-I", "-B", str(OUTILS / script), *arguments], capture_output=True,
                              text=True, encoding="utf-8", errors="replace", env=env, timeout=30)

    def test_placement_des_touches(self):
        with tempfile.TemporaryDirectory() as d:
            # LOCALAPPDATA d'essai : même un garde-fou cassé ne trouverait aucun vrai profil.
            sortie = self.lancer("placer_touches.py", env={**os.environ, "LOCALAPPDATA": d})
            self.assertNotEqual(sortie.returncode, 0)
            self.assertIn("--reel", sortie.stderr)
            self.assertEqual(list(Path(d).iterdir()), [])

    def test_pilote_du_pad(self):
        for commande in (["appui", "1"], ["releve"]):
            sortie = self.lancer("pad.py", *commande)
            self.assertNotEqual(sortie.returncode, 0, commande)
            self.assertIn("--reel", sortie.stderr, commande)


if __name__ == "__main__":
    unittest.main()
