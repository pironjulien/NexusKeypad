"""Note l'état d'une IA de la Trinité pour le pad (appelé par les hooks de Claude et d'Antigravity).

Usage : etat.py <agent> <evenement>   (le JSON du hook arrive sur stdin)
Écrit %USERPROFILE%\\.nexus\\trinite\\<agent>\\<session>.json = {"state", "at", "event"}.
États : run (travaille), wait (t'attend : question, accord, choix), done (fini), end (session fermée).
Les hooks de Claude tournent en tâche de fond, donc parfois dans le désordre : chaque appel note l'heure de son départ,
écrit sous un verrou par session, et ne remplace jamais un état noté par un événement parti après lui.
Un tour qui finit sur une question (« ? » final, AskUserQuestion, boîte de choix) est « wait », pas « done ».
Rend « {} » sur stdout (« {"decision": "allow"} » au PreToolUse d'Antigravity, qui refuse l'outil sans décision) pour
qu'un hook bloquant laisse l'agent continuer ; ne lève jamais.
"""
import json
import os
import sys
import time
import uuid
from pathlib import Path

RACINE = Path(os.environ.get("USERPROFILE", Path.home())) / ".nexus" / "trinite"

# Outils qui posent une question à l'utilisateur : le tour s'arrête sur lui.
QUESTIONS = {"AskUserQuestion", "ask_question"}  # Claude, Antigravity
CHOIX = "mcp__ccd_turn__wrap_up"  # boîte de fin de tour de l'appli : une question si « ask » est rempli


def derniere_reponse(transcript: str) -> str:
    """Texte du dernier message de l'assistant dans le journal du tour (fin du fichier seulement).

    Claude : lignes {"type": "assistant", "message": {...}} ; Antigravity : {"type": "PLANNER_RESPONSE", "content": ...},
    son transcriptPath pouvant être un dossier de morceaux (le plus récent fait foi).
    """
    chemin = Path(transcript) if transcript else None
    if chemin is not None and chemin.is_dir():
        morceaux = sorted(chemin.rglob("*.jsonl"), key=lambda p: p.stat().st_mtime)
        chemin = morceaux[-1] if morceaux else None
    if chemin is None:
        return ""
    transcript = str(chemin)
    try:
        with open(transcript, "rb") as f:
            f.seek(0, os.SEEK_END)
            f.seek(max(0, f.tell() - 256 * 1024))
            lignes = f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return ""
    for ligne in reversed(lignes):
        try:
            entree = json.loads(ligne)
        except ValueError:
            continue
        if entree.get("type") == "PLANNER_RESPONSE" and entree.get("content"):
            return str(entree["content"]).strip()
        message = entree.get("message") or {}
        if entree.get("type") != "assistant" or message.get("role") != "assistant":
            continue
        textes = [b.get("text", "") for b in message.get("content") or [] if isinstance(b, dict) and b.get("type") == "text"]
        if textes:
            return textes[-1].strip()
    return ""


def lancee_par_une_session(session_id: str) -> bool:
    """Session Claude lancée par une autre session (start_session : sa fiche porte « spawnedFrom ») : elle travaille pour
    Claude, qui rend compte à l'utilisateur ; ses fins de tour ne le préviennent pas (ni note, ni clignotement), comme le relais."""
    if not session_id:
        return False
    racine = Path(os.environ.get("LOCALAPPDATA", "")) / "Claude-Data"
    for fiche in racine.glob("*/claude-code-sessions/**/local_*.json"):
        try:
            texte = fiche.read_text(encoding="utf-8")
        except OSError:
            continue
        if session_id in texte:
            try:
                donnees = json.loads(texte)
            except ValueError:
                continue
            if donnees.get("cliSessionId") == session_id:
                return bool(donnees.get("spawnedFrom"))
    return False


def etat_pour(evenement: str, payload: dict, precedent: dict) -> str | None:
    if evenement == "PermissionRequest":
        return "perm"  # à l'instant même (la notification permission_prompt n'arrive qu'après ~6 s)
    if evenement == "Notification":
        kind = payload.get("notification_type") or ""
        message = (payload.get("message") or "").lower()
        return "perm" if kind == "permission_prompt" or "permission" in message else None
    if evenement == "PreToolUse":
        # Claude : tool_name ; Antigravity : toolCall.name.
        outil = payload.get("tool_name") or (payload.get("toolCall") or {}).get("name") or ""
        if outil in QUESTIONS:
            return "wait"
        if outil == CHOIX and (payload.get("tool_input") or {}).get("ask"):
            return "ask"  # affiché « travaille » ; devient « wait » au Stop
        return "run"
    # Antigravity n'a pas d'UserPromptSubmit : PreInvocation (avant chaque appel du modèle) marque la reprise d'un tour.
    if evenement in ("PostToolUse", "PostToolUseFailure"):  # l'outil a tourné (réussi ou non) : plus d'autorisation en attente
        outil = payload.get("tool_name") or (payload.get("toolCall") or {}).get("name") or ""
        if outil == CHOIX and precedent.get("state") == "ask":
            return None  # la boîte de fin pose sa question : « ask » doit tenir jusqu'au Stop
        return "run"
    if evenement in ("UserPromptSubmit", "PreInvocation", "PostInvocation"):
        return "run"
    if evenement == "StopFailure":
        # Claude : tour arrêté par une erreur d'API ; limite d'usage ou de facturation à part (rien à faire qu'attendre).
        erreur = payload.get("error") or payload.get("error_type")  # schéma officiel : error (quelques versions : error_type)
        return "limit" if erreur in ("rate_limit", "billing_error") else "error"
    if evenement == "Stop":
        if payload.get("fullyIdle") is False:
            return "run"  # Antigravity : un sous-agent travaille encore
        if lancee_par_une_session(str(payload.get("session_id") or "")):
            return "repos"  # travaille pour Claude : au repos sur le pad, jamais d'alerte
        # Une autorisation encore en attente n'a pas de Stop : après un refus, le tour se termine comme les autres.
        if precedent.get("state") in ("ask", "wait"):
            return "wait"
        reponse = derniere_reponse(payload.get("transcript_path") or payload.get("transcriptPath") or "")
        return "wait" if reponse.rstrip("*_ )»\"'").endswith("?") else "done"
    return None


def nom_session(payload: dict) -> str:
    brut = payload.get("session_id") or payload.get("conversationId") or payload.get("cascadeId") or "defaut"
    return "".join(c for c in str(brut) if c.isalnum() or c in "-_")[:80] or "defaut"


# Demande d'autorisation de Claude, à trancher depuis le pad (touches Approuver / Refuser) : déposée dans
# demandes/<session>.json, réponse du plugin dans demandes/<session>.decision.json. Appli de bureau seulement (la carte
# reste à l'écran, le premier qui décide l'emporte) ; l'attente cesse si la demande est tranchée à l'écran (la session
# quitte « perm » après elle) ou après 10 min. Même contrat que hook/Demandes.cs.
DELAI_DEMANDE = 600


def resume(payload: dict) -> str:
    entree = payload.get("tool_input") or {}
    for cle in ("command", "file_path", "path", "url", "description", "pattern"):
        valeur = entree.get(cle)
        if isinstance(valeur, str) and valeur.strip():
            return " ".join(valeur.split())[:80]
    return ""


def lire_json(fichier: Path) -> dict | None:
    try:
        return json.loads(fichier.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def attendre_demande(payload: dict, debut: float) -> str:
    if os.environ.get("CLAUDE_CODE_ENTRYPOINT") != "claude-desktop":
        return "{}"
    session = nom_session(payload)
    dossier = RACINE / "claude" / "demandes"
    dossier.mkdir(parents=True, exist_ok=True)
    demande, decision = dossier / f"{session}.json", dossier / f"{session}.decision.json"
    etat = RACINE / "claude" / f"{session}.json"
    ident = uuid.uuid4().hex
    temporaire = dossier / f"{session}.json.{os.getpid()}.tmp"
    temporaire.write_text(json.dumps({"id": ident, "session": session, "outil": payload.get("tool_name") or "",
                                      "resume": resume(payload), "at": debut}), encoding="utf-8")
    os.replace(temporaire, demande)
    try:
        fin = time.time() + DELAI_DEMANDE
        while time.time() < fin:
            time.sleep(0.2)
            d = lire_json(decision)
            if d and d.get("id") == ident and d.get("behavior") in ("allow", "deny"):
                reponse = {"behavior": d["behavior"]}
                if d["behavior"] == "deny":
                    reponse["message"] = "Refusé depuis le pad NexusKeypad."
                return json.dumps({"hookSpecificOutput": {"hookEventName": "PermissionRequest", "decision": reponse}})
            e = lire_json(etat)
            if e and float(e.get("at") or 0) > debut and e.get("state") != "perm":
                return "{}"  # tranchée à l'écran
            courante = lire_json(demande)
            if courante and courante.get("id") != ident:
                return "{}"  # remplacée par une demande plus récente de la même session
        return "{}"
    finally:
        for fichier in (demande, decision):
            contenu = lire_json(fichier)
            if contenu and contenu.get("id") == ident:
                fichier.unlink(missing_ok=True)


def noter(agent: str, evenement: str, payload: dict, debut: float) -> None:
    """Écrit l'état de la session, sous son verrou ; un événement parti avant celui déjà noté est ignoré."""
    dossier = RACINE / agent
    dossier.mkdir(parents=True, exist_ok=True)
    session = nom_session(payload)
    fichier = dossier / f"{session}.json"
    verrou = dossier / f"{session}.lock"
    for _ in range(1000):  # 20 s au plus (un Stop lit son journal) ; un verrou de plus de 30 s est celui d'un appel mort
        try:
            os.close(os.open(verrou, os.O_CREAT | os.O_EXCL | os.O_WRONLY))
            break
        except FileExistsError:
            try:
                if time.time() - verrou.stat().st_mtime > 30:
                    verrou.unlink(missing_ok=True)
            except OSError:
                pass
            time.sleep(0.02)
    else:
        return
    try:
        try:
            precedent = json.loads(fichier.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            precedent = {}
        if float(precedent.get("at") or 0) > debut:
            return  # un événement parti après celui-ci a déjà écrit
        etat = "end" if evenement == "SessionEnd" else etat_pour(evenement, payload, precedent)
        if etat is None:
            return
        temporaire = dossier / f"{session}.{os.getpid()}.tmp"
        temporaire.write_text(json.dumps({"state": etat, "at": debut, "event": evenement}), encoding="utf-8")
        os.replace(temporaire, fichier)
    finally:
        verrou.unlink(missing_ok=True)


def main() -> None:
    debut = time.time()
    agent, evenement = sys.argv[1], sys.argv[2]
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except (ValueError, OSError):
        payload = {}
    # Antigravity : son PreToolUse exige une décision (sans « allow », l'outil est REFUSÉ : vu le 23/10, tous ses outils
    # bloqués). Ailleurs, « {} » ne change rien (Claude, et Stop/PostToolUse/PreInvocation d'Antigravity).
    if agent == "claude" and evenement == "PermissionRequest":
        noter(agent, evenement, payload, debut)
        print(attendre_demande(payload, debut), flush=True)
        return
    print(json.dumps({"decision": "allow"}) if agent == "antigravity" and evenement == "PreToolUse" else "{}", flush=True)
    noter(agent, evenement, payload, debut)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # un hook ne doit jamais gêner l'agent
        pass
