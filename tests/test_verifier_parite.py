"""Le banc de parité ne doit jamais piloter une UI ni confondre présence et fonctionnement."""
import builtins
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("keypad_parity", ROOT / "outils/verifier_parite.py")
PARITY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PARITY)
NOW = datetime(2026, 10, 29, 12, tzinfo=timezone.utc)


def snapshot():
    return {"schema": 1, "source": "cached-pad-snapshot", "available": True,
            "sampled_at": NOW.isoformat(), "generated_at": NOW.isoformat(),
            "agents": [{"id": agent, "app_open": True, "active_conversations": 1, "state": "Travaille",
                        "conversations": [{"id": "session-1", "state": "Travaille", "title": "PRIVATE TITLE", "prompt": "PRIVATE PROMPT"}],
                        "quota": {"five_hour_used": None, "week_used": .5},
                        "connection": {"state": "Pret", "detail": "PRIVATE DETAIL"}} for agent in PARITY.AGENTS]}


class SafeParityTests(unittest.TestCase):
    def test_only_read_commands_and_no_false_native_success(self):
        commands = []
        def run(command, work):
            commands.append(command)
            value = snapshot() if command[-1] == "snapshot" else {"state": "Pret"}
            return {"returncode": 0, "stdout": json.dumps(value), "error": None}
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            report = PARITY.collect(ROOT, path / "home", path / "local", path, run=run, now=NOW)
        self.assertEqual(len(commands), 4)
        self.assertEqual(commands[0][-1], "snapshot")
        self.assertEqual([command[-2:] for command in commands[1:]], [[agent, "status"] for agent in PARITY.AGENTS])
        self.assertFalse(report["parity_proven"])
        self.assertFalse(report["ui_actions_executed"])
        self.assertFalse(report["keypad_buttons_pressed"])
        self.assertTrue(report["snapshot"]["available"])
        encoded = json.dumps(report)
        self.assertNotIn("PRIVATE", encoded)
        for agent in report["agents"]:
            self.assertEqual(agent["configuration"]["proof"], "configuration_only")
            self.assertEqual(agent["hook_cache"]["native_execution"], "not_proven_by_file_presence")
            self.assertTrue(all(check["status"] == "not_executed" for check in agent["ui_checks"].values()))

    def test_missing_or_failed_cache_remains_unavailable(self):
        for response in ({"error": "TimeoutExpired", "stdout": ""}, {"error": "command_failed", "stdout": json.dumps(snapshot())},
                         {"error": None, "stdout": "old plugin: unknown command"}, {"error": None, "stdout": "{broken"}):
            with self.subTest(response=response), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary)
                report = PARITY.collect(ROOT, path, path, path, run=lambda *_: response, now=NOW)
                self.assertFalse(report["snapshot"]["available"])
                self.assertFalse(report["parity_proven"])

    def test_malformed_fields_and_future_timestamp_do_not_pass(self):
        invalid = snapshot()
        invalid["agents"][0]["state"] = {"unexpected": "PRIVATE"}
        invalid["agents"][0]["conversations"] = None
        clean = PARITY.sanitise_snapshot(invalid)
        self.assertIsNone(clean["agents"][0]["state"])
        self.assertNotIn("PRIVATE", json.dumps(clean))
        invalid["sampled_at"] = "2026-10-30T12:00:00+00:00"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            report = PARITY.collect(ROOT, path, path, path,
                run=lambda *_: {"error": None, "stdout": json.dumps(invalid)}, now=NOW)
        self.assertFalse(report["snapshot"]["available"])
        self.assertEqual(report["snapshot"]["error"], "future_sample_timestamp")

    def test_first_activity_evidence_is_separate_from_ready_state(self):
        raw = snapshot()
        for entry, evidence in zip(raw["agents"], (False, True, "true")):
            entry["connection"]["first_activity_event_observed"] = evidence
        clean = PARITY.sanitise_snapshot(raw)
        self.assertEqual([entry["connection"]["first_activity_event_observed"] for entry in clean["agents"]], [False, True, None])
        self.assertTrue(all(entry["connection"]["state"] == "Pret" for entry in clean["agents"]))

    @unittest.skipUnless((ROOT / "outils/essais_reels.py").is_file(), "legacy bench stub is private and absent from the public export")
    def test_legacy_guard_runs_before_unsafe_imports(self):
        original_import = builtins.__import__
        def safe_import(name, *args, **kwargs):
            if name.split(".")[0] in {"asyncio", "ctypes", "websockets", "subprocess", "urllib"}:
                raise AssertionError("Legacy guard did not run before desktop automation imports")
            return original_import(name, *args, **kwargs)
        for environment in ({}, {"CODEX_THREAD_ID": "safe-test"}):
            for run_name in ("__main__", "imported_legacy_bench"):
                with self.subTest(run_name=run_name, environment=environment), mock.patch.dict(os.environ, environment, clear=True), \
                     mock.patch.object(sys, "argv", ["essais_reels.py", "--reel"]), mock.patch("builtins.__import__", side_effect=safe_import):
                    with self.assertRaises(SystemExit) as stopped:
                        runpy.run_path(str(ROOT / "outils/essais_reels.py"), run_name=run_name)
                self.assertIn("Banc historique retiré", str(stopped.exception))
                self.assertIn("nettoyage", str(stopped.exception))
                self.assertIn("verifier_parite.py", str(stopped.exception))

    def test_capture_is_opt_in_and_constrained_to_pad_cache(self):
        commands = []
        def run(command, work):
            commands.append(command)
            return {"error": None, "stdout": str(work / "outside.png") if command[-1] == "capture" else json.dumps(snapshot())}
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            report = PARITY.collect(ROOT, path, path, path, capture=True, run=run, now=NOW)
        self.assertEqual(commands[-1][-1], "capture")
        self.assertEqual(report["pad_capture"]["error"], "capture_unavailable")


if __name__ == "__main__":
    unittest.main()
