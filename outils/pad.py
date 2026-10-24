"""Pilote le pad Trinité pour les essais, par le tube local du plugin (Pilote.cs).

Usage : python -I outils/pad.py capture            → chemin de l'image des 9 touches
        python -I outils/pad.py appui <1-18>       → appui simulé (places du pad : 1 Codex, 2 Claude, 3 Antigravity, 4-6 : leurs quotas, 7 Capture, 8 Photo, 9 Dictée ; 10-18 : page 2)
        python -I outils/pad.py etat               → résumé de la dernière lecture
        python -I outils/pad.py releve             → relevé forcé du quota d'Antigravity (sa page Settings › Models)
"""
import ctypes, sys, time

TUBE = "\\\\.\\pipe\\nexus-trinite-pad"


def commande(texte: str) -> str:
    # Une commande à la fois. Tube occupé (autre client) : WaitNamedPipe attend qu'il se libère. Tube absent : le plugin le
    # recrée après chaque commande ; « plugin absent » seulement s'il manque plus d'une seconde d'affilée. 10 s au plus.
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    fin = time.monotonic() + 10
    absent_depuis = None
    while time.monotonic() < fin:
        try:
            tube = open(TUBE, "r+b", buffering=0)
        except OSError:
            if k32.WaitNamedPipeW(TUBE, 2000):
                absent_depuis = None
                continue  # libéré : nouvel essai
            erreur = ctypes.get_last_error()
            if erreur == 2:  # ERROR_FILE_NOT_FOUND
                absent_depuis = absent_depuis or time.monotonic()
                if time.monotonic() - absent_depuis > 1:
                    raise FileNotFoundError(TUBE)
                time.sleep(0.05)
            elif erreur != 121:  # ERROR_SEM_TIMEOUT : encore occupé ; toute autre erreur remonte
                raise
            continue
        # Connecté : la commande part une seule fois, jamais rejouée (un second « appui 9 » arrêterait la dictée lancée).
        with tube:
            tube.write((texte + "\n").encode("utf-8"))
            return tube.readline().decode("utf-8").strip()
    return "tube du pad occupé depuis 10 s"


if __name__ == "__main__":
    try:
        print(commande(" ".join(sys.argv[1:]) or "aide"))
    except FileNotFoundError:
        print("plugin du pad absent (service Logi arrêté, ou plugin non chargé : pad sur le logo Logi)")
        sys.exit(1)
