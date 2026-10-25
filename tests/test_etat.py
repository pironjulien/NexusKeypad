"""Règles de etat.py (hooks de Claude et d'Antigravity) : python -I -m unittest discover -s tests"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ETAT = Path(__file__).resolve().parents[1] / "etat" / "etat.py"
spec = importlib.util.spec_from_file_location("etat", ETAT)
etat = importlib.util.module_from_spec(spec)
spec.loader.exec_module(etat)


class Regles(unittest.TestCase):
    def test_reprise_de_tour(self):
        for evenement in ("UserPromptSubmit", "PreInvocation", "PostInvocation", "PostToolUse", "PreToolUse"):
            self.assertEqual(etat.etat_pour(evenement, {}, {"state": "wait"}), "run", evenement)

    def test_question_par_outil_ou_boite(self):
        self.assertEqual(etat.etat_pour("PreToolUse", {"tool_name": "AskUserQuestion"}, {}), "wait")
        self.assertEqual(etat.etat_pour("PreToolUse", {"tool_name": etat.CHOIX, "tool_input": {"ask": "On y va ?"}}, {}), "ask")
        self.assertEqual(etat.etat_pour("Stop", {}, {"state": "ask"}), "wait")
        # le PostToolUse de la boîte ne doit pas effacer sa question avant le Stop
        self.assertIsNone(etat.etat_pour("PostToolUse", {"tool_name": etat.CHOIX}, {"state": "ask"}))
        self.assertEqual(etat.etat_pour("PostToolUse", {"tool_name": "Bash"}, {"state": "wait"}), "run")  # après un accord

    def test_question_d_antigravity_par_son_outil(self):
        charge = {"toolCall": {"name": "ask_question", "args": {"questions": []}}}
        self.assertEqual(etat.etat_pour("PreToolUse", charge, {}), "wait")
        self.assertEqual(etat.etat_pour("PreToolUse", {"toolCall": {"name": "run_command"}}, {}), "run")

    def test_hooks_d_antigravity_au_bon_format(self):
        import importlib.util as iu
        chemin = Path(__file__).resolve().parents[1] / "outils" / "installer_hooks.py"
        spec_i = iu.spec_from_file_location("installer_hooks", chemin)
        installer = iu.module_from_spec(spec_i)
        spec_i.loader.exec_module(installer)
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / ".gemini" / "config").mkdir(parents=True)
            ancien = Path.home
            Path.home = classmethod(lambda cls: Path(d))
            try:
                installer.poser_antigravity()
            finally:
                Path.home = ancien
            hooks = json.loads((Path(d) / ".gemini" / "config" / "hooks.json").read_text(encoding="utf-8"))["trinite-pad"]
        self.assertEqual(set(hooks), {"PreInvocation", "PreToolUse", "PostToolUse", "Stop"})
        self.assertEqual(hooks["PreToolUse"][0]["matcher"], "*")  # outils : enveloppe { matcher, hooks }
        self.assertIn("command", hooks["PreToolUse"][0]["hooks"][0])
        self.assertIn("command", hooks["Stop"][0])  # sinon : gestionnaires directs
        self.assertIn("PostToolUse", installer.EVENEMENTS_CLAUDE)

    def test_installateur_garde_les_hooks_des_autres(self):
        import importlib.util as iu
        chemin = Path(__file__).resolve().parents[1] / "outils" / "installer_hooks.py"
        spec_i = iu.spec_from_file_location("installer_hooks", chemin)
        installer = iu.module_from_spec(spec_i)
        spec_i.loader.exec_module(installer)
        autre = {"type": "command", "command": "& share-session.ps1", "timeout": 30}
        ancien_pad = installer.commande_claude("SessionEnd")
        with tempfile.TemporaryDirectory() as d:
            reglages = Path(d) / "settings.json"
            reglages.write_text(json.dumps({"hooks": {"SessionEnd": [{"hooks": [ancien_pad, autre]}]}}), encoding="utf-8")
            installer.poser_claude(reglages)
            installer.poser_claude(reglages)  # idempotent
            groupes = json.loads(reglages.read_text(encoding="utf-8"))["hooks"]["SessionEnd"]
        commandes = [h["command"] for g in groupes for h in g["hooks"]]
        self.assertEqual(commandes.count("& share-session.ps1"), 1)  # gardé (perdu le 23/10)
        self.assertEqual(sum(installer.du_pad({"command": c}) for c in commandes), 1)  # le pad, une seule fois
        tiers = {"type": "command", "command": "pwsh -File audit.ps1 -Project NexusKeypad"}
        self.assertFalse(installer.du_pad(tiers))  # un autre outil qui cite le projet n'est pas retiré

    def test_installateur_sans_reglages_claude_et_chemin_avec_espace(self):
        import importlib.util as iu
        chemin = Path(__file__).resolve().parents[1] / "outils" / "installer_hooks.py"
        spec_i = iu.spec_from_file_location("installer_hooks", chemin)
        installer = iu.module_from_spec(spec_i)
        spec_i.loader.exec_module(installer)
        with tempfile.TemporaryDirectory() as d:
            reglages = Path(d) / "absent" / "settings.json"
            installer.poser_claude(reglages)  # réglages de Claude absents : créés
            self.assertIn("Stop", json.loads(reglages.read_text(encoding="utf-8"))["hooks"])
        self.assertEqual(installer.chaine_powershell("C:/Mes documents/l'pad"), "'C:/Mes documents/l''pad'")
        installer.ETAT, installer.sous_onedrive = Path("C:/Mes documents/l'pad/etat/etat.py"), lambda: None
        commande = installer.commande_claude("Stop")["command"]
        self.assertEqual(commande, "python -I -B 'C:\\Mes documents\\l''pad\\etat\\etat.py' claude Stop")  # -B : aucun cache dans OneDrive
        self.assertTrue(installer.du_pad({"command": commande}))

    def test_sous_agent_d_antigravity_encore_au_travail(self):
        self.assertEqual(etat.etat_pour("Stop", {"fullyIdle": False}, {}), "run")

    def test_fin_ou_question_selon_le_dernier_message(self):
        with tempfile.TemporaryDirectory() as d:
            journal = Path(d) / "t.jsonl"
            for texte, attendu in (("C'est fait.", "done"), ("On le pousse ?", "wait"), ("Tu valides **?**", "wait")):
                ligne = {"type": "assistant", "message": {"role": "assistant", "content": [{"type": "text", "text": texte}]}}
                journal.write_text(json.dumps(ligne) + "\n", encoding="utf-8")
                self.assertEqual(etat.etat_pour("Stop", {"transcript_path": str(journal)}, {}), attendu, texte)
            # Antigravity : PLANNER_RESPONSE dans un dossier de morceaux
            morceaux = Path(d) / "chunks"
            morceaux.mkdir()
            (morceaux / "00000001.jsonl").write_text(json.dumps({"type": "PLANNER_RESPONSE", "content": "Je relance ?"}) + "\n", encoding="utf-8")
            self.assertEqual(etat.etat_pour("Stop", {"transcriptPath": str(morceaux)}, {}), "wait")

    def test_permission_demandee(self):
        self.assertEqual(etat.etat_pour("Notification", {"notification_type": "permission_prompt"}, {}), "perm")
        self.assertEqual(etat.etat_pour("Stop", {}, {"state": "perm"}), "done")  # refusée : le tour finit, plus de cadenas
        self.assertEqual(etat.etat_pour("PostToolUse", {"tool_name": "Bash"}, {"state": "perm"}), "run")  # accordée

    def test_erreur_et_limite(self):
        self.assertEqual(etat.etat_pour("StopFailure", {"error": "rate_limit"}, {}), "limit")
        self.assertEqual(etat.etat_pour("StopFailure", {"error_type": "billing_error"}, {}), "limit")
        self.assertEqual(etat.etat_pour("StopFailure", {"error_type": "server_error"}, {}), "error")
        self.assertIn("StopFailure", installer_claude_evenements())
        self.assertIn("PostToolUseFailure", installer_claude_evenements())
        self.assertEqual(etat.etat_pour("PostToolUseFailure", {"tool_name": "Bash"}, {"state": "perm"}), "run")  # accordée, l'outil a échoué
        self.assertIsNone(etat.etat_pour("Notification", {"message": "idle"}, {}))

    def test_fichier_ecrit_et_retire(self):
        with tempfile.TemporaryDirectory() as d:
            env = {**os.environ, "USERPROFILE": d}
            lancer = lambda ev, charge: subprocess.run(
                [sys.executable, "-I", str(ETAT), "antigravity", ev], input=json.dumps(charge), text=True,
                capture_output=True, env=env, check=True,
            )
            sortie = lancer("PreInvocation", {"conversationId": "abc-1"})
            self.assertEqual(sortie.stdout.strip(), "{}")  # un hook bloquant laisse l'agent continuer
            # PreToolUse d'Antigravity : sans « allow », il REFUSE l'outil (tous ses outils bloqués le 23/10)
            decision = lancer("PreToolUse", {"conversationId": "abc-1", "toolCall": {"name": "run_command"}})
            self.assertEqual(json.loads(decision.stdout), {"decision": "allow"})
            fichier = Path(d) / ".nexus" / "trinite" / "antigravity" / "abc-1.json"
            self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], "run")
            lancer("SessionEnd", {"conversationId": "abc-1"})
            self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], "end")

    def test_evenement_en_retard_ignore(self):
        # Hooks en tâche de fond : un PreToolUse parti avant le Stop mais écrit après ne doit pas le remplacer.
        with tempfile.TemporaryDirectory() as d:
            ancienne = etat.RACINE
            etat.RACINE = Path(d)
            try:
                etat.noter("claude", "Stop", {"session_id": "s1"}, debut=200.0)
                etat.noter("claude", "PreToolUse", {"session_id": "s1", "tool_name": "Bash"}, debut=100.0)
                fichier = Path(d) / "claude" / "s1.json"
                self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], "done")
                etat.noter("claude", "SessionEnd", {"session_id": "s1"}, debut=300.0)
                etat.noter("claude", "PostToolUse", {"session_id": "s1", "tool_name": "Bash"}, debut=250.0)
                self.assertEqual(json.loads(fichier.read_text(encoding="utf-8"))["state"], "end")
                self.assertEqual([f.name for f in (Path(d) / "claude").iterdir()], ["s1.json"])  # ni verrou ni temporaire
            finally:
                etat.RACINE = ancienne


# Exécutable natif livré dans le paquet (hook/, sans Python) : mêmes règles que etat.py, cas par cas. test.ps1 le compile
# et donne son chemin ; lancé seul, ce test est sauté.
HOOK = os.environ.get("NEXUSKEYPAD_HOOK", "")


@unittest.skipUnless(HOOK and Path(HOOK).exists(), "nexuskeypad-hook.exe non compilé (lancer test.ps1)")
class Parite(unittest.TestCase):
    def test_memes_etats_que_etat_py(self):
        with tempfile.TemporaryDirectory() as d:
            journal = Path(d) / "t.jsonl"
            morceaux = Path(d) / "chunks"
            morceaux.mkdir()
            (morceaux / "00000001.jsonl").write_text(json.dumps({"type": "PLANNER_RESPONSE", "content": "Je relance ?"}) + "\n", encoding="utf-8")

            def ligne(texte):
                return json.dumps({"type": "assistant", "message": {"role": "assistant", "content": [{"type": "text", "text": texte}]}})

            cas = [
                ("claude", "UserPromptSubmit", {}, "wait", None),
                ("claude", "PreToolUse", {"tool_name": "AskUserQuestion"}, None, None),
                ("claude", "PreToolUse", {"tool_name": etat.CHOIX, "tool_input": {"ask": "On y va ?"}}, None, None),
                ("claude", "PreToolUse", {"tool_name": etat.CHOIX, "tool_input": {}}, None, None),
                ("claude", "PostToolUse", {"tool_name": etat.CHOIX}, "ask", None),
                ("claude", "PostToolUse", {"tool_name": "Bash"}, "perm", None),
                ("claude", "PostToolUseFailure", {"tool_name": "Bash"}, "perm", None),
                ("claude", "Notification", {"notification_type": "permission_prompt"}, None, None),
                ("claude", "Notification", {"message": "Claude needs your Permission"}, None, None),
                ("claude", "Notification", {"message": "idle"}, "run", None),
                ("claude", "StopFailure", {"error": "rate_limit"}, None, None),
                ("claude", "StopFailure", {"error_type": "billing_error"}, None, None),
                ("claude", "StopFailure", {"error_type": "server_error"}, None, None),
                ("claude", "Stop", {}, "ask", None),
                ("claude", "Stop", {}, "perm", None),
                ("claude", "Stop", {}, None, "C'est fait."),
                ("claude", "Stop", {}, None, "On le pousse ?"),
                ("claude", "Stop", {}, None, "Tu valides **?**"),
                ("claude", "Stop", {}, None, "« Prêt ? »"),
                ("claude", "SessionEnd", {}, "run", None),
                ("antigravity", "PreInvocation", {}, "done", None),
                ("antigravity", "PreToolUse", {"toolCall": {"name": "ask_question"}}, None, None),
                ("antigravity", "PreToolUse", {"toolCall": {"name": "run_command"}}, None, None),
                ("antigravity", "Stop", {"fullyIdle": False}, None, None),
                ("antigravity", "Stop", {"transcriptPath": str(morceaux)}, None, None),
                ("antigravity", "Inconnu", {}, "run", None),
            ]
            for i, (agent, evenement, charge, avant, texte) in enumerate(cas):
                charge = {**charge, "session_id" if agent == "claude" else "conversationId": f"s{i}"}
                if texte is not None:
                    journal.write_text(ligne(texte) + "\n", encoding="utf-8")
                    charge["transcript_path"] = str(journal)
                precedent = {"state": avant, "at": 1.0, "event": "x"} if avant else {}
                dossier = Path(d) / ".nexus" / "trinite" / agent
                dossier.mkdir(parents=True, exist_ok=True)
                fichier = dossier / f"s{i}.json"
                if avant:
                    fichier.write_text(json.dumps(precedent), encoding="utf-8")
                attendu = "end" if evenement == "SessionEnd" else etat.etat_pour(evenement, charge, precedent)
                sortie = subprocess.run([HOOK, agent, evenement], input=json.dumps(charge), text=True, encoding="utf-8",
                                        capture_output=True, env={**os.environ, "USERPROFILE": d}, check=True)
                voulu = {"decision": "allow"} if agent == "antigravity" and evenement == "PreToolUse" else {}
                self.assertEqual(json.loads(sortie.stdout), voulu, (agent, evenement))
                obtenu = json.loads(fichier.read_text(encoding="utf-8"))["state"] if fichier.exists() else None
                self.assertEqual(obtenu, attendu if attendu is not None else avant, (agent, evenement, charge, avant, texte))
            self.assertEqual({f.suffix for f in (Path(d) / ".nexus" / "trinite" / "claude").iterdir()}, {".json"})  # ni verrou ni temporaire

    def test_evenement_en_retard_ignore(self):
        with tempfile.TemporaryDirectory() as d:
            dossier = Path(d) / ".nexus" / "trinite" / "claude"
            dossier.mkdir(parents=True)
            (dossier / "s1.json").write_text(json.dumps({"state": "done", "at": 9e12, "event": "Stop"}), encoding="utf-8")
            subprocess.run([HOOK, "claude", "PreToolUse"], input=json.dumps({"session_id": "s1", "tool_name": "Bash"}),
                           text=True, capture_output=True, env={**os.environ, "USERPROFILE": d}, check=True)
            self.assertEqual(json.loads((dossier / "s1.json").read_text(encoding="utf-8"))["state"], "done")
            self.assertEqual([f.name for f in dossier.iterdir()], ["s1.json"])  # ni verrou ni temporaire

    def test_entree_vide_ou_illisible(self):
        with tempfile.TemporaryDirectory() as d:
            for entree in ("", "pas du json"):
                sortie = subprocess.run([HOOK, "claude", "Stop"], input=entree, text=True, capture_output=True,
                                        env={**os.environ, "USERPROFILE": d}, check=True)
                self.assertEqual(sortie.stdout.strip(), "{}")
            self.assertEqual(json.loads((Path(d) / ".nexus" / "trinite" / "claude" / "defaut.json").read_text(encoding="utf-8"))["state"], "done")


def installer_claude_evenements():
    import importlib.util as iu
    chemin = Path(__file__).resolve().parents[1] / "outils" / "installer_hooks.py"
    spec = iu.spec_from_file_location("installer_hooks", chemin)
    module = iu.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.EVENEMENTS_CLAUDE


if __name__ == "__main__":
    unittest.main()

