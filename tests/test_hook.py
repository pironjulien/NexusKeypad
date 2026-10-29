"""Règles du hook natif nexuskeypad-hook.exe (états en direct de Claude et d'Antigravity, échange de la mise à jour).
test.ps1 le compile et donne son chemin (NEXUSKEYPAD_HOOK) ; lancé seul, ce test est sauté.
python -I -m unittest discover -s tests"""
import json
import os
import shutil
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

HOOK = os.environ.get("NEXUSKEYPAD_HOOK", "")
CHOIX = "mcp__ccd_turn__wrap_up"  # boîte de fin de tour de l'appli : une question si « ask » est rempli


def setUpModule():
    if not HOOK or not Path(HOOK).exists():
        return  # les classes expliquent comment compiler le hook
    racine = Path(__file__).resolve().parents[1]
    sources = [*racine.joinpath("hook").glob("*.cs"), *racine.joinpath("hook").glob("*.csproj"),
               racine / "hook" / "Directory.Build.props", racine / "plugin" / "src" / "Trinite" / "EtatsEnDirect.cs"]
    perimees = [p.relative_to(racine).as_posix() for p in sources if p.stat().st_mtime_ns > Path(HOOK).stat().st_mtime_ns]
    if perimees:
        raise RuntimeError(f"Hook natif de test périmé ({', '.join(perimees)}) : lancer test.ps1 avant les tests.")

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
    ("antigravity", "Stop", {"terminationReason": "USER_CANCELED"}, "run", None, "repos"),
    ("antigravity", "Stop", {"terminationReason": "USER_CANCELED", "fullyIdle": False}, "run", None, "run"),
    ("antigravity", "PreToolUse", {"toolCall": {"name": "ask_question"}}, None, None, "wait"),
    ("antigravity", "PreToolUse", {"toolCall": {"name": "run_command"}}, None, None, "run"),
    ("antigravity", "Stop", {"fullyIdle": False}, None, None, "run"),  # un sous-agent travaille encore
    ("antigravity", "Stop", {"transcriptPath": "<morceaux>"}, None, None, "wait"),  # PLANNER_RESPONSE finissant par « ? »
    ("antigravity", "Inconnu", {}, "run", None, "run"),
]


def lancer(d, *arguments, entree="{}"):
    environnement = {**os.environ, "USERPROFILE": d, "LOCALAPPDATA": str(Path(d) / "local")}
    environnement.pop("CLAUDE_CONFIG_DIR", None)
    return subprocess.run([HOOK, *arguments], input=entree, text=True, encoding="utf-8", capture_output=True,
                          env=environnement, check=True)


@unittest.skipUnless(HOOK and Path(HOOK).exists(), "nexuskeypad-hook.exe non compilé (lancer test.ps1)")
class Etats(unittest.TestCase):
    def test_connecteur_desactive_ne_publie_plus(self):
        with tempfile.TemporaryDirectory() as d:
            recus = Path(d) / "local" / "NexusKeypad" / "connecteurs" / "etats"
            recus.mkdir(parents=True)
            for agent in ("claude", "antigravity"):
                (recus / f"{agent}.json").write_text('{"active":false}', encoding="utf-8")
                sortie = lancer(d, agent, "PreToolUse", entree='{"session_id":"s1","conversationId":"s1"}')
                self.assertEqual(json.loads(sortie.stdout), {"decision": "allow"} if agent == "antigravity" else {})
                self.assertFalse((Path(d) / ".nexus" / "trinite" / agent / "s1.json").exists())

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
    l'ancien. Dossiers temporaires seulement (jamais le vrai dossier des plugins, ni le vrai service Logi : hors du vrai
    dossier, seuls les processus nommés par NEXUSKEYPAD_SWAP_PROCESSES sont arrêtés)."""

    @staticmethod
    def paquet(dossier, version):
        (dossier / "metadata").mkdir(parents=True)
        (dossier / "metadata" / "LoupedeckPackage.yaml").write_text(f"type: plugin4\nversion: {version}\n", encoding="utf-8")

    def echanger(self, d, nouveau_present=True, tenir=None, env=None):
        racine = Path(d)
        cible, nouveau, ancien = racine / "Plugins" / "NexusKeypad", racine / "maj" / "nouveau", racine / "maj" / "ancien"
        self.paquet(cible, "1.3.0")
        nouveau.parent.mkdir(parents=True)
        if nouveau_present:
            self.paquet(nouveau, "1.3.1")
        etat = racine / "status" / "keypad.json"
        tenu = tenir(cible) if tenir else None  # le dossier tenu comme par le service Logi (DLL chargées)
        try:
            sortie = subprocess.run([HOOK, "swap", str(nouveau), str(cible), str(ancien), str(etat), "1.3.1"],
                                    capture_output=True, timeout=90,
                                    env={**os.environ, "NEXUSKEYPAD_SWAP_PROCESSES": "", "NEXUSKEYPAD_SWAP_AGENT": "", **(env or {})})
        finally:
            if tenu is not None and hasattr(tenu, "close"):
                tenu.close()
        return sortie.returncode, cible, ancien, json.loads(etat.read_text(encoding="utf-8"))

    @staticmethod
    def version(cible):
        return (cible / "metadata" / "LoupedeckPackage.yaml").read_text(encoding="utf-8").split("version: ")[1].strip()

    def test_reussi(self):
        with tempfile.TemporaryDirectory() as d:
            code, cible, ancien, etat_ = self.echanger(d)
            self.assertEqual(code, 0)
            self.assertEqual(self.version(cible), "1.3.1")
            self.assertFalse(ancien.exists(), "sauvegarde effacée")
            self.assertEqual((etat_["state"], etat_["version"], etat_["product"]), ("done", "1.3.1", "keypad"))

    def test_echec_remet_l_ancien(self):
        with tempfile.TemporaryDirectory() as d:
            code, cible, ancien, etat_ = self.echanger(d, nouveau_present=False)
            self.assertEqual(code, 1)
            self.assertEqual(self.version(cible), "1.3.0", "ancienne version remise")
            self.assertEqual((etat_["state"], etat_["version"], etat_["latest"]), ("failed", "1.3.0", "1.3.1"), "la version en place, pas null")
            self.assertIn("putting the new version in place", etat_["message"])

    def test_dossier_tenu_sans_service_a_arreter(self):
        # 27/10 : fichier du plugin tenu, rien à arrêter : échec, ancienne version en place et dite, message lisible
        # (pas la clé de ressource brute « UnauthorizedAccess_IODenied_Path »).
        with tempfile.TemporaryDirectory() as d:
            tenir = lambda cible: open(cible / "metadata" / "LoupedeckPackage.yaml", "rb")
            code, cible, ancien, etat_ = self.echanger(d, tenir=tenir, env={"NEXUSKEYPAD_SWAP_PROCESSES": ""})
            self.assertEqual(code, 1)
            self.assertEqual(self.version(cible), "1.3.0")
            self.assertEqual((etat_["state"], etat_["version"]), ("failed", "1.3.0"))
            self.assertIn("moving the installed plugin aside", etat_["message"])
            self.assertNotRegex(etat_["message"], r"[A-Za-z]+_[A-Za-z]+_", "clé de ressource brute")

    def test_dossier_tenu_service_arrete_puis_relance(self):
        # Le service Logi tient le dossier : il est arrêté le temps de l'échange, puis l'agent relancé détaché. Ici un faux
        # service (copie de cmd.exe, dossier courant dans le plugin) et un faux agent qui laisse une trace.
        with tempfile.TemporaryDirectory() as d:
            faux = Path(d) / "outils" / "fauxlogi.exe"
            faux.parent.mkdir()
            shutil.copyfile(Path(os.environ["SystemRoot"]) / "System32" / "cmd.exe", faux)
            trace = Path(d) / "relance.txt"
            service = []

            def tenir(cible):
                service.append(subprocess.Popen([str(faux), "/k"], cwd=cible, stdin=subprocess.PIPE,
                                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
                time.sleep(0.5)
                return None

            env = {"NEXUSKEYPAD_SWAP_PROCESSES": "fauxlogi", "NEXUSKEYPAD_SWAP_AGENT": f'"{faux}" /c echo relance>"{trace}"'}
            try:
                code, cible, ancien, etat_ = self.echanger(d, tenir=tenir, env=env)
                self.assertEqual(code, 0, etat_)
                self.assertEqual(self.version(cible), "1.3.1")
                self.assertEqual((etat_["state"], etat_["version"]), ("done", "1.3.1"))
                self.assertIsNotNone(service[0].poll(), "faux service arrêté")
                for _ in range(50):
                    if trace.exists():
                        break
                    time.sleep(0.1)
                self.assertTrue(trace.exists(), "faux agent relancé")
            finally:
                for p in service:
                    if p.poll() is None:
                        p.kill()
                    p.wait()
                    p.stdin.close()
                # Le faux agent, relancé détaché, écrit sa trace avant de finir : tant qu'il tourne, fauxlogi.exe ne
                # s'efface pas et le dossier temporaire non plus (échec intermittent, 27/10). Attendu, sinon arrêté.
                self.attendre_fin("fauxlogi.exe")

    @staticmethod
    def attendre_fin(image, secondes=10):
        def present():
            liste = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {image}", "/NH", "/FO", "CSV"],
                                   capture_output=True, text=True, errors="replace").stdout
            return f'"{image}"'.lower() in liste.lower()

        fin = time.monotonic() + secondes
        while present() and time.monotonic() < fin:
            time.sleep(0.1)
        if present():
            subprocess.run(["taskkill", "/F", "/IM", image], capture_output=True)
            time.sleep(0.5)


if __name__ == "__main__":
    unittest.main()
