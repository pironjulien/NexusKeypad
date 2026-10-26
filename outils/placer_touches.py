"""Place les 18 touches du plugin Trinité sur le profil général du MX Keypad (Loupedeck70) : la Trinité en page 1
(t1 à t9), le poste en page 2 (t10 à t18 : volume, son coupé, Gmail, Agenda, Messages, santé du PC, Store et Windows
Update, site). Les deux boutons du bas passent d'une page à l'autre. Une page cachée ne coûte rien : le plugin ne la relit ni ne la
dessine tant que le service ne demande pas ses images.

À lancer service Logi arrêté (sinon il réécrit le profil). Sauvegarde ProfileInfo.json avant d'écrire.
Usage : python -I placer_touches.py --reel [--retour]   (--retour remet la sauvegarde)
--reel exigé : le script écrit le vrai profil d'Options+ (garde-fou du 25/10 : aucun essai sur l'environnement réel
sans un drapeau explicite).
"""
import copy
import json
import os
import shutil
import sys
import uuid
from pathlib import Path

PROFILS = Path(os.environ["LOCALAPPDATA"]) / "Logi" / "LogiPluginService" / "Applications" / "Loupedeck70" / "@_defaultwin" / "Profiles"
ACTION = "$NexusKeypad___Loupedeck.TrinitePlugin.ToucheCommand___t{}"


def main() -> None:
    if "--reel" not in sys.argv:
        sys.exit("placer_touches.py écrit le vrai profil d'Options+ : ajouter --reel pour le faire pour de bon.")
    fichier = next(PROFILS.glob("*/ProfileInfo.json"))
    sauvegarde = fichier.with_name("ProfileInfo.avant-trinite.json")
    if "--retour" in sys.argv:
        shutil.copy2(sauvegarde, fichier)
        print(f"profil remis depuis {sauvegarde}")
        return
    if not sauvegarde.exists():
        shutil.copy2(fichier, sauvegarde)
    profil = json.loads(fichier.read_text(encoding="utf-8-sig"))
    page = profil["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0]
    places = 0
    for controle in page["controls"]:
        if 0 <= controle["controlId"] <= 8:
            controle["pressAction"] = ACTION.format(controle["controlId"] + 1)
            places += 1
    pages = profil["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"]
    if len(pages) == 1:
        # Même gabarit de touches que la page d'origine de Logi, renommé.
        origine = json.loads(sauvegarde.read_text(encoding="utf-8-sig"))
        page2 = copy.deepcopy(origine["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0])
        page2["name"], page2["displayName"] = uuid.uuid4().hex.upper(), "Page (2)"
        pages.append(page2)
    for controle in pages[1]["controls"]:
        if 0 <= controle["controlId"] <= 8:
            controle["pressAction"] = ACTION.format(controle["controlId"] + 10)
            places += 1
    fichier.write_text(json.dumps(profil, ensure_ascii=False, indent=4), encoding="utf-8")
    print(f"{places} touches placées dans {fichier}")


if __name__ == "__main__":
    main()
