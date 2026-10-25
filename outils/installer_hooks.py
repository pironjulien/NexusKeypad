"""Branche etat.py sur les hooks de Claude et d'Antigravity (idempotent).

Usage : python -I installer_hooks.py [<reglages-claude.json>...]   (ajoute aussi le hook d'Antigravity s'il est installé)
Sans argument : Antigravity seul. Un fichier de réglages de Claude absent est créé.
Claude : un groupe par événement, en tâche de fond (async) pour ne jamais ralentir un tour.
Antigravity : entrée « trinite-pad » de ~/.gemini/config/hooks.json (son format : type, command, timeout).
"""
import json
import os
import re
import sys
from pathlib import Path

ETAT = Path(__file__).resolve().parents[1] / "etat" / "etat.py"


def sous_onedrive() -> str | None:
    """Chemin d'etat.py relatif à OneDrive quand le dépôt y est : le même réglage sert sur tous les PC synchronisés."""
    racine = os.environ.get("OneDrive")
    if not racine:
        return None
    try:
        return str(ETAT.relative_to(Path(racine).resolve()))
    except ValueError:
        return None

# PostToolUse : après un accord ou une réponse de l'utilisateur, le tour reprend (sinon « t'attend » restait jusqu'au Stop).
EVENEMENTS_CLAUDE = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SessionEnd"]
# Antigravity (antigravity.google/docs/hooks) : ni UserPromptSubmit ni Notification ; PreInvocation, avant chaque appel du
# modèle, marque la reprise d'un tour.
EVENEMENTS_AGY = ["PreInvocation", "PreToolUse", "PostToolUse", "Stop"]


def du_pad(hook: dict) -> bool:
    """Hook posé par cet installateur : il appelle etat.py du pad (pas un autre outil qui citerait le projet)."""
    return re.search(r"etat[\\/]etat\.py'?\)? (claude|antigravity) ", hook.get("command", "") + " ") is not None


def chaine_powershell(texte: str) -> str:
    """Chaîne PowerShell entre apostrophes : espaces tenus, apostrophe doublée."""
    return "'" + texte.replace("'", "''") + "'"


def sans_espace(chemin: Path) -> str | None:
    """Chemin d'etat.py sans espace pour Antigravity (qui ne passe pas les guillemets) : tel quel, sinon son nom court
    Windows (8.3) ; None si le volume n'en a pas."""
    if " " not in str(chemin):
        return str(chemin)
    if os.name == "nt":
        import ctypes

        tampon = ctypes.create_unicode_buffer(32768)
        if ctypes.windll.kernel32.GetShortPathNameW(str(chemin), tampon, len(tampon)) and " " not in tampon.value:
            return tampon.value
    return None


def commande_claude(evenement: str) -> dict:
    relatif = sous_onedrive()
    script = f"(Join-Path $env:OneDrive {chaine_powershell(relatif)})" if relatif else chaine_powershell(str(ETAT))
    return {
        "type": "command",
        "shell": "powershell",
        "command": f"python -I -B {script} claude {evenement}",
        "async": True,
        "timeout": 10,
    }


def poser_claude(chemin: Path) -> None:
    reglages = json.loads(chemin.read_text(encoding="utf-8-sig")) if chemin.exists() else {}
    chemin.parent.mkdir(parents=True, exist_ok=True)
    hooks = reglages.setdefault("hooks", {})
    for evenement in EVENEMENTS_CLAUDE:
        groupes = hooks.setdefault(evenement, [])
        voulu = commande_claude(evenement)
        # Retire seulement les hooks du pad : un groupe peut porter aussi ceux d'autres outils (share-session.ps1 en
        # fin de session, perdu le 23/10 quand le groupe entier était retiré). Un groupe vidé disparaît.
        for groupe in groupes:
            groupe["hooks"] = [h for h in groupe.get("hooks", []) if not du_pad(h)]
        groupes[:] = [g for g in groupes if g.get("hooks")]
        groupes.append({"hooks": [voulu]})
    chemin.write_text(json.dumps(reglages, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"hooks Claude posés : {chemin}")


def poser_antigravity() -> None:
    chemin = Path.home() / ".gemini" / "config" / "hooks.json"
    if not chemin.parent.exists():
        return
    hooks = json.loads(chemin.read_text(encoding="utf-8-sig")) if chemin.exists() else {}
    relatif = sous_onedrive()
    # Sans guillemets : Antigravity les passerait tels quels à python. Chemin avec espace : nom court Windows.
    script = os.path.join("%OneDrive%", relatif) if relatif and " " not in os.environ["OneDrive"] + relatif else sans_espace(ETAT)
    if script is None:
        print(f"hooks Antigravity non posés : chemin avec espace sans nom court ({ETAT}) ; déplacez le pad", file=sys.stderr)
        return
    def gestionnaire(e: str) -> dict:
        return {"type": "command", "command": f"python -I -B {script} antigravity {e}", "timeout": 10}

    # Événements d'outil : enveloppe { matcher, hooks } (matcher = expression régulière, « * » = tous les outils) ;
    # PreInvocation et Stop : gestionnaires directement sous l'événement.
    hooks["trinite-pad"] = {
        e: [{"matcher": "*", "hooks": [gestionnaire(e)]}] if e.endswith("ToolUse") else [gestionnaire(e)] for e in EVENEMENTS_AGY
    }
    chemin.write_text(json.dumps(hooks, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"hooks Antigravity posés : {chemin}")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        poser_claude(Path(argument))
    poser_antigravity()
