"""Contrat d'exécution passif des hooks Codex ; profils temporaires seulement, aucun vrai événement fabriqué."""
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

HOOK = os.environ.get("NEXUSKEYPAD_HOOK")


@unittest.skipUnless(HOOK and Path(HOOK).exists(), "nexuskeypad-hook.exe non compilé (lancer test.ps1)")
class CodexPassif(unittest.TestCase):
    def lancer(self, racine, evenement, charge):
        environnement = {**os.environ, "USERPROFILE": str(racine), "LOCALAPPDATA": str(racine / "local")}
        entree = json.dumps(charge) if isinstance(charge, dict) else charge
        retour = subprocess.run([HOOK, "codex", evenement], input=entree, text=True, encoding="utf-8",
                                capture_output=True, env=environnement, timeout=3, check=True)
        self.assertEqual(json.loads(retour.stdout), {})
        self.assertEqual(retour.stderr, "")
        return retour

    def test_sorties_ne_decident_jamais(self):
        with tempfile.TemporaryDirectory() as dossier:
            racine = Path(dossier)
            transcript = racine / "transcript.jsonl"
            transcript.write_text(json.dumps({"type": "session_meta", "payload": {
                "id": "s1", "source": "vscode", "thread_source": "user"}}) + "\n", encoding="utf-8")
            commun = {"session_id": "s1", "turn_id": "t1", "transcript_path": str(transcript)}
            for evenement, detail in [
                ("SessionStart", {}), ("UserPromptSubmit", {}),
                ("PreToolUse", {"tool_use_id": "a", "tool_name": "Bash"}),
                ("PermissionRequest", {"tool_name": "Bash"}),
                ("PostToolUse", {"tool_use_id": "a", "tool_name": "Bash"}),
                ("Stop", {"last_assistant_message": "Fini."}), ("Interrupt", {}), ("SessionEnd", {})
            ]:
                with self.subTest(evenement=evenement):
                    self.lancer(racine, evenement, {**commun, **detail})
                    fichier = racine / ".nexus" / "trinite" / "codex" / "s1.json"
                    attendu = {"UserPromptSubmit": "run", "PermissionRequest": "perm", "PostToolUse": "run", "Stop": "done"}.get(evenement)
                    if attendu:
                        self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], attendu)
            fichier = racine / ".nexus" / "trinite" / "codex" / "s1.json"
            self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], "end")

    def test_entree_incomplete_ne_cree_pas_etat(self):
        with tempfile.TemporaryDirectory() as dossier:
            racine = Path(dossier)
            for charge in ["", "{", "[]", {}, {"session_id": "../s1", "turn_id": "t1"}]:
                with self.subTest(charge=charge):
                    self.lancer(racine, "Stop", charge)
            self.assertFalse((racine / ".nexus" / "trinite" / "codex").exists())

    def test_desactivation_conserve_flux_et_coupe_observation(self):
        with tempfile.TemporaryDirectory() as dossier:
            racine = Path(dossier)
            recus = racine / "local" / "NexusKeypad" / "connecteurs" / "etats"
            recus.mkdir(parents=True)
            recu = recus / "codex.json"
            recu.write_text('{"active":true,"identity":"nexus-keypad@personal"}', encoding="utf-8")
            profil = racine / ".codex"
            profil.mkdir()
            (profil / "config.toml").write_text('[plugins."nexus-keypad@personal"]\nenabled = true\n', encoding="utf-8")
            transcript = racine / "transcript.jsonl"
            transcript.write_text(json.dumps({"type": "session_meta", "payload": {
                "id": "s1", "source": "vscode", "thread_source": "user"}}) + "\n", encoding="utf-8")
            self.lancer(racine, "UserPromptSubmit", {"session_id": "s1", "turn_id": "t1", "transcript_path": str(transcript)})
            fichier = racine / ".nexus" / "trinite" / "codex" / "s1.json"
            avant = fichier.read_bytes()
            recu.write_text('{"active":false,"identity":"nexus-keypad@personal"}', encoding="utf-8")
            self.lancer(racine, "PreToolUse", {"session_id": "s1", "turn_id": "t1", "transcript_path": str(transcript),
                                             "tool_use_id": "a", "tool_name": "Bash"})
            self.assertEqual(fichier.read_bytes(), avant)
