namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading;

    // Un geste au plus. Les attentes relisent seulement la page : un timeout de transport ne rejoue jamais le clic.
    public static class NavigationAntigravity
    {
        public static Boolean Ouvrir(String conversation)
        {
            var horloge = Stopwatch.StartNew();
            String Lire() => Cdp.Evaluer(Cdp.PortAntigravity, Js.NavigationAntigravity(conversation), attente: 1)
                is { ValueKind: JsonValueKind.String } valeur ? valeur.GetString() : null;
            return Executer(Lire,
                () => Cdp.Evaluer(Cdp.PortAntigravity, Js.OuvrirAntigravity(conversation, route: true), attente: 2)
                    is { ValueKind: JsonValueKind.True },
                () => horloge.ElapsedMilliseconds, Thread.Sleep);
        }

        public static Boolean Executer(Func<String> lire, Func<Boolean> ouvrir, Func<Int64> maintenant, Action<Int32> pause)
        {
            var fin = maintenant() + 5000;
            String etat;
            do
            {
                etat = lire();
                if (etat == "open") { return true; }
                if (etat == "ready") { break; }
                if (maintenant() >= fin) { return false; }
                pause(100);
            }
            while (maintenant() < fin);

            if (etat != "ready" || !ouvrir()) { return false; }
            fin = maintenant() + 4000;
            do
            {
                if (lire() == "open") { return true; }
                if (maintenant() >= fin) { return false; }
                pause(100);
            }
            while (maintenant() < fin);
            return false;
        }
    }
}
