"""Les anciens hooks sonores déjà chargés ne doublent pas les notes des plugins migrés."""
import io
import json
import os
import runpy
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).resolve().parents[1] / "sons" / "jouer.py"


@unittest.skipUnless(SCRIPT.exists(), "sons/ est privé et absent du paquet public")
class MigrationSon(unittest.TestCase):
    def lancer(self, charge, recu=None, agent="antigravity"):
        with tempfile.TemporaryDirectory() as dossier:
            if recu is not None:
                cible = Path(dossier) / "NexusKeypad" / "connecteurs" / "etats" / f"{agent}.json"
                cible.parent.mkdir(parents=True)
                cible.write_text(json.dumps(recu), encoding="utf-8")
            popen = Mock()
            son = types.SimpleNamespace(PlaySound=Mock(), SND_FILENAME=0)
            sortie = io.StringIO()
            arguments = [str(SCRIPT), agent, *(["--hook"] if agent == "antigravity" else [])]
            with patch.dict(os.environ, {"LOCALAPPDATA": dossier}), patch.dict(sys.modules, {"winsound": son}), \
                    patch.object(sys, "argv", arguments), \
                    patch.object(sys, "stdin", io.StringIO(json.dumps(charge))), patch.object(sys, "stdout", sortie), \
                    patch("subprocess.Popen", popen):
                try:
                    runpy.run_path(str(SCRIPT), run_name="__main__")
                except SystemExit as fin:
                    self.assertEqual(fin.code, 0)
            if agent == "antigravity":
                self.assertEqual(json.loads(sortie.getvalue()), {})
                son.PlaySound.assert_not_called()
                return popen
            self.assertEqual(sortie.getvalue(), "")
            popen.assert_not_called()
            return son.PlaySound

    def test_hook_deja_charge_devient_silencieux_apres_migration(self):
        for agent in ("claude", "antigravity"):
            for actif in (True, False):
                with self.subTest(agent=agent, actif=actif):
                    self.lancer({"fullyIdle": True}, {"hookOwner": "plugin", "active": actif}, agent).assert_not_called()

    def test_installation_historique_garde_sa_note(self):
        self.lancer({"fullyIdle": True}).assert_called_once()
        self.lancer({"session_id": "ancienne-session"}, agent="claude").assert_called_once()

    def test_installation_historique_annulee_ou_encore_active_reste_silencieuse(self):
        for charge in ({"fullyIdle": False}, {"fullyIdle": True, "terminationReason": "USER_CANCELED"}):
            with self.subTest(charge=charge):
                self.lancer(charge).assert_not_called()

    def test_ancien_stop_claude_annule_reste_silencieux_apres_migration(self):
        self.lancer({"session_id": "ancienne-session", "terminationReason": "USER_CANCELED"},
                    {"hookOwner": "plugin", "active": True}, "claude").assert_not_called()

    def test_plugin_prive_claude_garde_huit_evenements_sans_ancien_python(self):
        plugin = SCRIPT.parents[1] / "nexus-keypad" / "hooks" / "hooks.json"
        if not plugin.exists():
            self.skipTest("plugin Claude privé absent")
        paquet = json.loads(plugin.read_text(encoding="utf-8"))
        self.assertEqual(set(paquet["hooks"]), {"Stop", "StopFailure", "UserPromptSubmit", "PreToolUse",
                                              "Notification", "SessionEnd", "PostToolUse", "PostToolUseFailure"})
        self.assertEqual(paquet["modules"], ["./register.tsx"])
        stop = paquet["hooks"]["Stop"][0]["hooks"]
        self.assertEqual(len(stop), 1)
        self.assertIn("nexuskeypad-hook.exe", stop[0]["command"])
        self.assertNotIn("python", plugin.read_text(encoding="utf-8").lower())
        self.assertNotIn("onedrive", plugin.read_text(encoding="utf-8").lower())
