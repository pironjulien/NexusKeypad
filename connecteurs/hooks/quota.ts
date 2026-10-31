import type { EngineInterface, Register, SessionMeasureInput } from 'claude-code'

// Claude's exact usage for the NexusKeypad pad: the 5-hour and weekly windows, as the app measures them, written to
// ~/.nexus/trinite/quota-claude.json on every change. No network, no interface: that file is the only write. The
// session is recorded: the pad keeps only the usage of a session of the signed-in account (several Claude accounts
// on one PC).
type Window = { kind: string; percentUsed: number; resetsAt?: string }

// Home folder, deduced from where the module lives: installed from a catalog (~/.claude/plugins) or by the pad's
// own plugin (%LOCALAPPDATA%\NexusKeypad\claude-marketplace). Anywhere else nothing is guessed: nothing is written.
function home($: EngineInterface): string | null {
  const root = $.plugin.root.replaceAll('\\', '/')
  for (const mark of ['/.claude/', '/AppData/Local/NexusKeypad/']) {
    if (root.includes(mark)) return root.split(mark)[0] ?? null
  }
  return null
}

async function write($: EngineInterface, windows: Window[] | undefined): Promise<void> {
  if (!windows) return // no measure yet
  const window = (kind: string) => {
    const w = windows.find(x => x.kind === kind)
    return w ? { pourcent: w.percentUsed, remise: w.resetsAt ?? null } : null
  }
  // The pad reads these French keys (cinq, semaine, pourcent, remise): they are the file's format.
  const cinq = window('five_hour')
  const semaine = window('seven_day')
  if (!cinq && !semaine) return // no subscription, or no answer yet
  const folder = home($)
  if (!folder) return
  const text = JSON.stringify({ at: new Date().toISOString(), session: await $.session.id(), cinq, semaine })
  await $.fs.write(`${folder}/.nexus/trinite/quota-claude.json`, text)
}

// Exported for register.tsx: the app refuses the whole module when on('session.start') is registered twice without a
// filter, so its single start handler calls quotaAtStart instead of registering this one.
export async function quotaAtStart($: EngineInterface): Promise<void> {
  await write($, (await $.session.usage()).rateLimits).catch(() => undefined)
}

export async function quotaMeasure($: EngineInterface, e: SessionMeasureInput): Promise<void> {
  if (e.changed.includes('rateLimits')) await write($, e.rateLimits).catch(() => undefined)
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const done = await next(e)
    await quotaAtStart($)
    return done
  })

  on('session.measure', async ($, e, next) => {
    await quotaMeasure($, e)
    return next(e)
  })
}
