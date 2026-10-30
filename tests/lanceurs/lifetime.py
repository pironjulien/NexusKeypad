"""Opt-in : vrai broker du pad sous job KILL_ON_JOB_CLOSE, uniquement une fixture headless locale."""
import ctypes as c
from ctypes import wintypes as w
import json
import os
from pathlib import Path
import subprocess
import shutil
import sys
import tempfile
import time


class Basic(c.Structure):
    _fields_ = [("process_time", c.c_int64), ("job_time", c.c_int64), ("flags", w.DWORD),
                ("min_ws", c.c_size_t), ("max_ws", c.c_size_t), ("active", w.DWORD),
                ("affinity", c.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]


class IO(c.Structure):
    _fields_ = [(name, c.c_uint64) for name in ("ro", "wo", "oo", "rb", "wb", "ob")]


class Extended(c.Structure):
    _fields_ = [("basic", Basic), ("io", IO), ("process_memory", c.c_size_t),
                ("job_memory", c.c_size_t), ("peak_process", c.c_size_t), ("peak_job", c.c_size_t)]


def main():
    fixture = Path(os.environ["NEXUSKEYPAD_LAUNCH_FIXTURE"]).resolve(strict=True)
    api = c.WinDLL("kernel32", use_last_error=True)
    signatures = {
        "CreateJobObjectW": ([c.c_void_p, w.LPCWSTR], w.HANDLE),
        "SetInformationJobObject": ([w.HANDLE, c.c_int, c.c_void_p, w.DWORD], w.BOOL),
        "AssignProcessToJobObject": ([w.HANDLE, w.HANDLE], w.BOOL),
        "OpenProcess": ([w.DWORD, w.BOOL, w.DWORD], w.HANDLE),
        "IsProcessInJob": ([w.HANDLE, w.HANDLE, c.POINTER(w.BOOL)], w.BOOL),
        "WaitForSingleObject": ([w.HANDLE, w.DWORD], w.DWORD),
        "TerminateProcess": ([w.HANDLE, w.UINT], w.BOOL),
        "CloseHandle": ([w.HANDLE], w.BOOL),
    }
    for name, (args, result) in signatures.items():
        function = getattr(api, name)
        function.argtypes, function.restype = args, result
    job = api.CreateJobObjectW(None, None)
    assert job, c.get_last_error()
    info = Extended()
    info.basic.flags = 0x2000
    assert api.SetInformationJobObject(job, 9, c.byref(info), c.sizeof(info)), c.get_last_error()
    worker = child = service = None
    with tempfile.TemporaryDirectory(prefix="nexuskeypad-lifetime-") as directory:
        gate, oracle, stop, receipt = [Path(directory) / name for name in ("gate", "oracle.json", "stop", "launch.json")]
        try:
            if len(sys.argv) == 2:
                # Le vrai échangeur natif doit sortir de son job restrictif pour recréer un faux agent Logi.
                hook = Path(sys.argv[1]).resolve(strict=True)
                root = Path(directory)
                target, incoming, backup = root / "Plugins" / "NexusKeypad", root / "new", root / "old"
                for package, version in ((target, "1.0.0"), (incoming, "1.0.1")):
                    (package / "metadata").mkdir(parents=True)
                    (package / "metadata" / "LoupedeckPackage.yaml").write_text(f"version: {version}\n")
                fake = root / ("nexuslogi" + str(os.getpid()) + ".exe")
                shutil.copyfile(Path(os.environ["SystemRoot"]) / "System32" / "cmd.exe", fake)
                service = subprocess.Popen([str(fake), "/k"], cwd=target, stdin=subprocess.PIPE,
                                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=0x08000000)
                time.sleep(0.2)
                agent = subprocess.list2cmdline([str(fixture), "--lifetime-child", str(oracle), str(stop),
                                                "a b", "", 'quote"value', "end\\", "é & % $(literal)"])
                env = {**os.environ, "NEXUSKEYPAD_SWAP_PROCESSES": fake.stem, "NEXUSKEYPAD_SWAP_AGENT": agent}
                worker = subprocess.Popen([str(hook), "swap", str(incoming), str(target), str(backup), str(receipt), "1.0.1"],
                                          creationflags=0x08000000, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
            else:
                worker = subprocess.Popen([str(fixture), "--lifetime-parent", str(gate), str(oracle), str(stop), str(receipt)],
                                          creationflags=0x08000000, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            assert api.AssignProcessToJobObject(job, int(worker._handle)), c.get_last_error()
            gate.touch()
            for _ in range(150):
                if oracle.exists() and receipt.exists():
                    break
                if worker.poll() not in (None, 0):
                    raise AssertionError({"code": worker.returncode, "status": receipt.read_text(encoding="utf-8") if receipt.exists() else None,
                                          "output": worker.communicate()})
                time.sleep(0.1)
            assert oracle.exists() and receipt.exists(), "La fixture du pad n'a pas été lancée."
            data = json.loads(oracle.read_text(encoding="utf-8"))
            assert Path(data["executable"]).resolve() == fixture, "Inspection limitée à notre propre fixture."
            assert data["values"] == ["a b", "", 'quote"value', "end\\", "é & % $(literal)"]
            result = json.loads(receipt.read_text(encoding="utf-8"))
            assert result.get("state") == "done" if len(sys.argv) == 2 else result["launched"] is True
            child = api.OpenProcess(0x100000 | 0x1000 | 1, False, data["pid"])
            assert child, c.get_last_error()
            inside = w.BOOL()
            assert api.IsProcessInJob(child, job, c.byref(inside)), c.get_last_error()
            assert api.WaitForSingleObject(child, 0) == 258
            api.CloseHandle(job)
            job = None
            worker.wait(timeout=5)
            survived = api.WaitForSingleObject(child, 1000) == 258
            assert not inside.value and survived, "L'application doit survivre au job du pad."
            stop.touch()
            assert api.WaitForSingleObject(child, 5000) == 0, "Fin normale de la fixture attendue."
            print(json.dumps({"outsideCallerJob": not bool(inside.value), "survivedJobClose": survived,
                              "exactArguments": True, "normalFixtureExit": True,
                              "source": "native-swap-wmi" if len(sys.argv) == 2 else "explorer-broker"}))
        finally:
            stop.touch(exist_ok=True)
            if child:
                if api.WaitForSingleObject(child, 1000) == 258:
                    api.TerminateProcess(child, 1)  # seulement le PID attesté de notre fixture
                api.CloseHandle(child)
            if job:
                api.CloseHandle(job)
            if worker and worker.poll() is None:
                worker.kill()
                worker.wait(timeout=5)
            if worker:
                worker.communicate()
            if service:
                if service.poll() is None:
                    service.kill()
                service.wait(timeout=5)
                service.stdin.close()


if __name__ == "__main__":
    main()
