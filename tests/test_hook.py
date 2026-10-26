"""Règles du hook natif nexuskeypad-hook.exe (états en direct de Claude et d'Antigravity, échange de la mise à jour).
test.ps1 le compile et donne son chemin (NEXUSKEYPAD_HOOK) ; lancé seul, ce test est sauté.
python -I -m unittest discover -s tests"""
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

HOOK = os.environ.get("NEXUSKEYPAD_HOOK", "")
CHOIX = "mcp__ccd_turn__wrap_up"  # boîte de fin de tour de l'appli : une question si « ask » est rempli

# (agent, événement, charge, état d'avant, dernier message du tour, état attendu). Relevés sur etat.py, la version
# Python d'origine retirée le 26/10 (même travail, mêmes règles).
CAS = [
    ("claude", "UserPromptSubmit", {}, "wait", None, "run"),  # reprise de tour
    ("claude", "PreToolUse", {"tool_name": "Bash"}, "wait", None, "run"),
    ("claude", "PreToolUse", {"tool_name": "AskUserQuestion"}, None, None, "wait"),
    ("claude", "PreToolUse", {"tool_name": CHOIX, "tool_input": {"ask": "On y va ?"}}, None, None, "ask"),
    ("claude", "PreToolUse", {"tool_name": CHOIX, "tool_input": {}}, None, None, "run"),
    ("claude", "PostToolUse", {"tool_name": CHOIX}, "ask", None, "ask"),  # la boîte garde sa question jusqu'au Stop
    ("claude", "PostToolUse", {"tool_name": "Bash"}, "perm", None, "run"),  # permission accordée
    ("claude", "PostToolUseFailure", {"tool_name": "Bash"}, "perm", None, "run"),  # accordée, l'outil a échoué
    ("claude", "Notification", {"notification_type": "permission_prompt"}, None, None, "perm"),
    ("claude", "Notification", {"message": "Claude needs your Permission"}, None, None, "perm"),
    ("claude", "Notification", {"message": "idle"}, "run", None, "run"),
    ("claude", "StopFailure", {"error": "rate_limit"}, None, None, "limit"),
    ("claude", "StopFailure", {"error_type": "billing_error"}, None, None, "limit"),
    ("claude", "StopFailure", {"error_type": "server_error"}, None, None, "error"),
    ("claude", "Stop", {}, "ask", None, "wait"),
    ("claude", "Stop", {}, "perm", None, "done"),  # refusée : le tour finit, plus de cadenas
    ("claude", "Stop", {}, None, "C'est fait.", "done"),
    ("claude", "Stop", {}, None, "On le pousse ?", "wait"),
    ("claude", "Stop", {}, None, "Tu valides **?**", "wait"),
    ("claude", "Stop", {}, None, "« Prêt ? »", "wait"),
    ("claude", "SessionEnd", {}, "run", None, "end"),
    ("antigravity", "PreInvocation", {}, "done", None, "run"),
    ("antigravity", "PostToolUse", {}, "wait", None, "run"),
    ("antigravity", "PreToolUse", {"toolCall": {"name": "ask_question"}}, None, None, "wait"),
    ("antigravity", "PreToolUse", {"toolCall": {"name": "run_command"}}, None, None, "run"),
    ("antigravity", "Stop", {"fullyIdle": False}, None, None, "run"),  # un sous-agent travaille encore
    ("antigravity", "Stop", {"transcriptPath": "<morceaux>"}, None, None, "wait"),  # PLANNER_RESPONSE finissant par « ? »
    ("antigravity", "Inconnu", {}, "run", None, "run"),
]


def lancer(d, *arguments, entree="{}"):
    return subprocess.run([HOOK, *arguments], input=entree, text=True, encoding="utf-8", capture_output=True,
                          env={**os.environ, "USERPROFILE": d}, check=True)


@unittest.skipUnless(HOOK and Path(HOOK).exists(), "nexuskeypad-hook.exe non compilé (lancer test.ps1)")
class Etats(unittest.TestCase):
    def test_regles(self):
        with tempfile.TemporaryDirectory() as d:
            journal = Path(d) / "t.jsonl"
            morceaux = Path(d) / "chunks"
            morceaux.mkdir()
            (morceaux / "00000001.jsonl").write_text(json.dumps({"type": "PLANNER_RESPONSE", "content": "Je relance ?"}) + "\n", encoding="utf-8")
            for i, (agent, evenement, charge, avant, texte, attendu) in enumerate(CAS):
                charge = {k: (str(morceaux) if v == "<morceaux>" else v) for k, v in charge.items()}
                charge["session_id" if agent == "claude" else "conversationId"] = f"s{i}"
                if texte is not None:
                    ligne = {"type": "assistant", "message": {"role": "assistant", "content": [{"type": "text", "text": texte}]}}
                    journal.write_text(json.dumps(ligne) + "\n", encoding="utf-8")
                    charge["transcript_path"] = str(journal)
                dossier = Path(d) / ".nexus" / "trinite" / agent
                dossier.mkdir(parents=True, exist_ok=True)
                fichier = dossier / f"s{i}.json"
                if avant:
                    fichier.write_text(json.dumps({"state": avant, "at": 1.0, "event": "x"}), encoding="utf-8")
                sortie = lancer(d, agent, evenement, entree=json.dumps(charge))
                # PreToolUse d'Antigravity : sans « allow », il REFUSE l'outil (tous ses outils bloqués le 23/10)
                voulu = {"decision": "allow"} if agent == "antigravity" and evenement == "PreToolUse" else {}
                self.assertEqual(json.loads(sortie.stdout), voulu, (agent, evenement))
                obtenu = json.loads(fichier.read_text(encoding="utf-8"))["state"] if fichier.exists() else None
                self.assertEqual(obtenu, attendu, (agent, evenement, charge, avant, texte))
            self.assertEqual({f.suffix for f in (Path(d) / ".nexus" / "trinite" / "claude").iterdir()}, {".json"})  # ni verrou ni temporaire

    def test_evenement_en_retard_ignore(self):
        # Hooks en tâche de fond : un PreToolUse parti avant le Stop mais écrit après ne doit pas le remplacer.
        with tempfile.TemporaryDirectory() as d:
            dossier = Path(d) / ".nexus" / "trinite" / "claude"
            dossier.mkdir(parents=True)
            (dossier / "s1.json").write_text(json.dumps({"state": "done", "at": 9e12, "event": "Stop"}), encoding="utf-8")
            lancer(d, "claude", "PreToolUse", entree=json.dumps({"session_id": "s1", "tool_name": "Bash"}))
            self.assertEqual(json.loads((dossier / "s1.json").read_text(encoding="utf-8"))["state"], "done")
            self.assertEqual([f.name for f in dossier.iterdir()], ["s1.json"])  # ni verrou ni temporaire

    def test_entree_vide_ou_illisible(self):
        with tempfile.TemporaryDirectory() as d:
            for entree in ("", "pas du json"):
                self.assertEqual(lancer(d, "claude", "Stop", entree=entree).stdout.strip(), "{}")
            self.assertEqual(json.loads((Path(d) / ".nexus" / "trinite" / "claude" / "defaut.json").read_text(encoding="utf-8"))["state"], "done")


@unittest.skipUnless(HOOK and Path(HOOK).exists(), "nexuskeypad-hook.exe non compilé (lancer test.ps1)")
class Echange(unittest.TestCase):
    """Mise à jour de l'édition complète : l'exécutable échange le dossier du plugin, détaché du plugin ; un échec remet
    l'ancien. Dossiers temporaires seulement (jamais le vrai dossier des plugins)."""

    def echanger(self, d, nouveau_present=True):
        racine = Path(d)
        cible, nouveau, ancien = racine / "Plugins" / "NexusKeypad", racine / "maj" / "nouveau", racine / "maj" / "ancien"
        cible.mkdir(parents=True)
        (cible / "version.txt").write_text("1.3.0", encoding="utf-8")
        if nouveau_present:
            nouveau.mkdir(parents=True)
            (nouveau / "version.txt").write_text("1.3.1", encoding="utf-8")
        etat = racine / "status" / "keypad.json"
        sortie = subprocess.run([HOOK, "swap", str(nouveau), str(cible), str(ancien), str(etat), "1.3.1"], capture_output=True, timeout=60)
        return sortie.returncode, cible, ancien, json.loads(etat.read_text(encoding="utf-8"))

    def test_reussi(self):
        with tempfile.TemporaryDirectory() as d:
            code, cible, ancien, etat_ = self.echanger(d)
            self.assertEqual(code, 0)
            self.assertEqual((cible / "version.txt").read_text(encoding="utf-8"), "1.3.1")
            self.assertFalse(ancien.exists(), "sauvegarde effacée")
            self.assertEqual((etat_["state"], etat_["version"], etat_["product"]), ("done", "1.3.1", "keypad"))

    def test_echec_remet_l_ancien(self):
        with tempfile.TemporaryDirectory() as d:
            code, cible, ancien, etat_ = self.echanger(d, nouveau_present=False)
            self.assertEqual(code, 1)
            self.assertEqual((cible / "version.txt").read_text(encoding="utf-8"), "1.3.0", "ancienne version remise")
            self.assertEqual(etat_["state"], "failed")


if __name__ == "__main__":
    unittest.main()
