import type { EngineInterface, Register, SessionMeasureInput } from 'claude-code'

// Quota exact de Claude pour le pad NexusKeypad : les fenêtres de 5 h et de la semaine, telles que l'appli les mesure,
// écrites dans ~/.nexus/trinite/quota-claude.json à chaque changement. Aucun réseau, aucune interface : seule écriture,
// ce fichier. La session est notée : le pad ne garde que le quota d'une session du compte connecté (plusieurs comptes
// Claude sur le PC).
type Fenetre = { kind: string; percentUsed: number; resetsAt?: string }

// Dossier personnel, déduit de l'endroit où vit le mod : posé par le plugin du pad
// (%LOCALAPPDATA%\NexusKeypad\claude-marketplace), ou installé depuis un catalogue (~/.claude/plugins). Ailleurs, on ne
// devine pas : rien n'est écrit.
function maison($: EngineInterface): string | null {
  const racine = $.plugin.root.replaceAll('\\', '/')
  for (const repere of ['/.claude/', '/AppData/Local/NexusKeypad/']) {
    if (racine.includes(repere)) return racine.split(repere)[0] ?? null
  }
  return null
}

async function ecrire($: EngineInterface, fenetres: Fenetre[] | undefined): Promise<void> {
  if (!fenetres) return // pas encore de mesure
  const fenetre = (kind: string) => {
    const f = fenetres.find(w => w.kind === kind)
    return f ? { pourcent: f.percentUsed, remise: f.resetsAt ?? null } : null
  }
  const cinq = fenetre('five_hour')
  const semaine = fenetre('seven_day')
  if (!cinq && !semaine) return // hors abonnement, ou pas encore de réponse
  const dossier = maison($)
  if (!dossier) return
  const texte = JSON.stringify({ at: new Date().toISOString(), session: await $.session.id(), cinq, semaine })
  await $.fs.write(`${dossier}/.nexus/trinite/quota-claude.json`, texte)
}

// Exportés pour le plugin privé nexus-keypad : l'appli refuse tout le module quand on('session.start') y est inscrit
// deux fois sans filtre ; son unique démarrage appelle donc quotaAuDemarrage au lieu d'inscrire ce register.
export async function quotaAuDemarrage($: EngineInterface): Promise<void> {
  await ecrire($, (await $.session.usage()).rateLimits).catch(() => undefined)
}

export async function quotaMesure($: EngineInterface, e: SessionMeasureInput): Promise<void> {
  if (e.changed.includes('rateLimits')) await ecrire($, e.rateLimits).catch(() => undefined)
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const done = await next(e)
    await quotaAuDemarrage($)
    return done
  })

  on('session.measure', async ($, e, next) => {
    await quotaMesure($, e)
    return next(e)
  })
}
