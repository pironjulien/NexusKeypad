"""Audit de parité en lecture seule du vrai plugin NexusKeypad.

Ne pilote aucune application, ne presse aucune touche, ne lit aucune UI et n'envoie rien.
Le snapshot vient du cache déjà constitué par le moteur du pad. Les statuts du helper
prouvent uniquement la configuration ; toute action UI demeure explicitement non exécutée.
Rapport : %LOCALAPPDATA%/NexusKeypad/parite/ ; --capture ajoute une image du cache du pad.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
from typing import Callable

AGENTS = ("claude", "codex", "antigravity")
STATES = {"Ferme", "Repos", "Travaille", "Attend", "Fini", "Autorisation", "Erreur", "Limite"}
CONNECTIONS = {"Absent", "Activer", "Relancer", "Reparer", "Pret", "EnCours", "Erreur"}
UI_FEATURES = ("open_app", "open_exact_conversation", "open_usage", "dictation", "completion_sound",
               "photo_attachment", "screen_attachment", "disable_and_reenable")
EVENTS = {"SessionStart", "SessionEnd", "UserPromptSubmit", "PreInvocation", "PostInvocation", "PreToolUse",
          "PermissionRequest", "PostToolUse", "PostToolUseFailure", "Stop", "StopFailure", "Interrupt",
          "Notification", "SubagentStart", "SubagentStop"}


def read_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError, UnicodeError):
        return None


def digest(path: Path):
    try:
        return hashlib.sha256(path.read_bytes()).hexdigest()
    except OSError:
        return None


def iso(value):
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed.astimezone(timezone.utc).isoformat() if parsed.tzinfo else None
    except (TypeError, ValueError, AttributeError, OverflowError):
        return None


def number(value):
    try:
        return value if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) else None
    except OverflowError:
        return None


def identifier(value):
    return value if isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9_-]{1,128}", value) else None


def known(value, choices):
    return value if isinstance(value, str) and value in choices else None


def sanitise_snapshot(value):
    """Liste fermée : un futur champ titre/prompt du pilote ne doit jamais entrer dans le rapport."""
    if not isinstance(value, dict) or value.get("schema") != 1 or value.get("source") != "cached-pad-snapshot":
        return {"available": False, "error": "unsupported_or_missing_snapshot"}
    agents = []
    for agent in value.get("agents", []) if isinstance(value.get("agents"), list) else []:
        if not isinstance(agent, dict) or known(agent.get("id"), AGENTS) is None:
            continue
        conversations = []
        for conversation in agent.get("conversations", []) if isinstance(agent.get("conversations"), list) else []:
            if not isinstance(conversation, dict):
                continue
            conversations.append({"id": identifier(conversation.get("id")),
                "state": known(conversation.get("state"), STATES),
                "since": iso(conversation.get("since")), "activity_at": iso(conversation.get("activity_at")),
                "seen": conversation.get("seen") is True, "relay": conversation.get("relay") is True})
        quota = agent.get("quota")
        connection = agent.get("connection")
        agents.append({"id": agent["id"], "app_open": agent.get("app_open") is True,
            "active_conversations": number(agent.get("active_conversations")),
            "state": known(agent.get("state"), STATES),
            "conversations": conversations,
            "quota": {"five_hour_used": number(quota.get("five_hour_used")), "week_used": number(quota.get("week_used"))}
                if isinstance(quota, dict) else None,
            "connection": {"state": known(connection.get("state"), CONNECTIONS),
                "prepared_at": iso(connection.get("prepared_at")),
                "first_activity_event_observed": connection.get("first_activity_event_observed")
                    if type(connection.get("first_activity_event_observed")) is bool else None} if isinstance(connection, dict) else None})
    dictation = value.get("dictation") if isinstance(value.get("dictation"), dict) else {}
    available = value.get("available") is True and iso(value.get("sampled_at")) is not None and len(agents) == 3 and {agent["id"] for agent in agents} == set(AGENTS)
    return {"schema": 1, "source": "cached-pad-snapshot", "available": available,
        "sampled_at": iso(value.get("sampled_at")), "generated_at": iso(value.get("generated_at")),
        "screen_off": value.get("screen_off") is True,
        "foreground_agent": known(value.get("foreground_agent"), AGENTS),
        "dictation": {"active": dictation.get("active") is True,
            "agent": known(dictation.get("agent"), AGENTS)}, "agents": agents}


def execute(command, cwd: Path, timeout=12):
    try:
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace",
                                stdin=subprocess.DEVNULL, cwd=cwd, timeout=timeout,
                                creationflags=0x08000000 if os.name == "nt" else 0)
        return {"returncode": result.returncode, "stdout": result.stdout, "error": None if result.returncode == 0 else "command_failed"}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {"returncode": None, "stdout": "", "error": type(error).__name__}


def decoded(result):
    try:
        return json.loads(result.get("stdout", ""))
    except (ValueError, TypeError):
        return None


def package(root: Path, manifest_relative: str):
    manifest = read_json(root / manifest_relative)
    if not isinstance(manifest, dict):
        return {"present": False}
    version = manifest.get("version")
    events = []
    for relative in ("hooks.json", "hooks/hooks.json"):
        hooks = read_json(root / relative)
        if isinstance(hooks, dict):
            for source in (hooks, hooks.get("hooks"), *hooks.values()):
                if isinstance(source, dict):
                    events.extend(key for key in source if key in EVENTS)
    return {"present": True, "version": version if isinstance(version, str) and re.fullmatch(r"[0-9A-Za-z.+_-]{1,80}", version) else None,
        "manifest_sha256": digest(root / manifest_relative), "skill_present": (root / "skills/nexus-keypad/SKILL.md").is_file(),
        "declared_hook_events": sorted(set(events)), "runtime_loaded": "not_verified"}


def installation(agent: str, home: Path, local: Path, claude_config: Path):
    if agent == "codex":
        root = home / ".codex/plugins/nexus-keypad"
        result = package(root, ".codex-plugin/plugin.json")
        result["cache_candidates"] = [package(path.parent.parent, ".codex-plugin/plugin.json") for path in sorted(
            (home / ".codex/plugins/cache").glob("*/nexus-keypad/*/.codex-plugin/plugin.json"))]
        return result
    if agent == "antigravity":
        return package(home / ".gemini/config/plugins/nexus-keypad", "plugin.json")
    installed = read_json(claude_config / "plugins/installed_plugins.json")
    plugins = installed.get("plugins", {}) if isinstance(installed, dict) else {}
    plugins = plugins if isinstance(plugins, dict) else {}
    result = {"present": False, "cache_candidates": []}
    for identity in ("nexus-keypad@nexus", "nexus-keypad@nexuskeypad"):
        entries = plugins.get(identity, [])
        for entry in entries if isinstance(entries, list) else []:
            if not isinstance(entry, dict) or not isinstance(entry.get("installPath"), str):
                continue
            root = Path(entry["installPath"]).resolve()
            allowed = (claude_config / "plugins/cache", local / "NexusKeypad/connecteurs/claude")
            if not any(root.is_relative_to(base.resolve()) for base in allowed):
                continue
            result["cache_candidates"].append(package(root, ".claude-plugin/plugin.json"))
    result["present"] = any(candidate["present"] for candidate in result["cache_candidates"])
    return result


def hook_cache(agent: str, home: Path):
    """État de fichiers seulement : ce relevé ne certifie ni leur origine native ni la confiance de l'hôte."""
    try:
        files = sorted((home / ".nexus/trinite" / agent).glob("*.json"), key=lambda path: path.stat().st_mtime, reverse=True)[:3]
    except OSError:
        files = []
    result = []
    for path in files:
        value = read_json(path)
        if not isinstance(value, dict):
            continue
        result.append({"session_id": identifier(path.stem), "at": number(value.get("at")),
            "event": known(value.get("event"), EVENTS) or "unknown",
            "state": known(value.get("state"), {"run", "perm", "wait", "ask", "done", "repos", "end", "error", "limit"}),
            "scope": "main" if value.get("scope") == "main" else None})
    return {"state_files": result, "native_execution": "not_proven_by_file_presence", "host_approval": "not_verified"}


def collect(repo: Path, home: Path, local: Path, work: Path, *, capture=False, run: Callable = execute,
            now=None, claude_config=None):
    now = now or datetime.now(timezone.utc)
    claude_config = claude_config or home / ".claude"
    pad = repo / "outils/pad.py"
    helper = local / "NexusKeypad/hook/nexuskeypad-hook.exe"
    result = run([sys.executable, "-I", "-B", str(pad), "snapshot"], work)
    snapshot = sanitise_snapshot(decoded(result))
    snapshot["transport_error"] = result.get("error")
    if result.get("error"):
        snapshot["available"] = False
    matrix = []
    for agent in AGENTS:
        status_result = run([str(helper), "connecteur", agent, "status"], work)
        raw_status = decoded(status_result)
        raw_status = raw_status if isinstance(raw_status, dict) else {}
        state = known(raw_status.get("state"), CONNECTIONS)
        cached = next((entry for entry in snapshot.get("agents", []) if entry["id"] == agent), None)
        matrix.append({"agent": agent, "configuration": {"state": state,
            "proof": "configuration_only" if state and not status_result.get("error") else "unavailable", "transport_error": status_result.get("error")},
            "package": installation(agent, home, local, claude_config), "hook_cache": hook_cache(agent, home),
            "pad_cache": cached, "ui_checks": {feature: {"status": "not_executed", "reason": "read_only_audit"} for feature in UI_FEATURES}})
    report = {"schema": 1, "product": "NexusKeypad", "generated_at": now.astimezone(timezone.utc).isoformat(),
        "mode": "read_only_audit", "ui_actions_executed": False, "keypad_buttons_pressed": False,
        "parity_proven": False, "snapshot": snapshot, "agents": matrix}
    sampled = iso(snapshot.get("sampled_at"))
    report["cache_age_seconds"] = (now - datetime.fromisoformat(sampled)).total_seconds() if sampled else None
    if report["cache_age_seconds"] is not None and report["cache_age_seconds"] < 0:
        snapshot["available"] = False
        snapshot["error"] = "future_sample_timestamp"
    if capture:
        captured = run([sys.executable, "-I", "-B", str(pad), "capture"], work)
        candidate = Path(captured.get("stdout", "").strip()).resolve()
        base = (local / "NexusKeypad/captures").resolve()
        if not captured.get("error") and candidate.is_relative_to(base) and candidate.is_file():
            report["pad_capture"] = {"path": str(candidate), "sha256": digest(candidate), "proof": "rendered_pad_cache_only"}
        else:
            report["pad_capture"] = {"error": captured.get("error") or "capture_unavailable"}
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", action="store_true", help="ajouter une capture du cache graphique du pad")
    args = parser.parse_args(argv)
    repo = Path(__file__).resolve().parents[1]
    home = Path(os.environ.get("USERPROFILE", str(Path.home())))
    local = Path(os.environ.get("LOCALAPPDATA", str(home / "AppData/Local")))
    output = local / "NexusKeypad/parite"
    output.mkdir(parents=True, exist_ok=True)
    claude = Path(os.environ.get("CLAUDE_CONFIG_DIR", str(home / ".claude")))
    report = collect(repo, home, local, output, capture=args.capture, claude_config=claude)
    name = "parite-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S-%fZ") + ".json"
    path = output / name
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)
    print(json.dumps({"report": str(path), "mode": report["mode"], "parity_proven": False,
                      "snapshot_available": report["snapshot"].get("available", False)}, ensure_ascii=False))
    return 0 if report["snapshot"].get("available") else 1


if __name__ == "__main__":
    sys.exit(main())
