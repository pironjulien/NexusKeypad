import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { Relais } from '../types'
import { quotaAtStart, quotaMeasure } from './quota'

// NexusKeypad in Claude, one plugin. Line under the prompt: Nexus Relay (peers reachable, messages waiting). No alert
// bubble, no network, no program started. The exact usage for the pad is written by quota.ts.
const minute = atom({ plugin: 'nexus-keypad', key: 'minute' } as const, 0)
const relais = atom({ plugin: 'nexus-keypad', key: 'relais' } as const, null)

// Nexus Relay, read from disk without starting anything (~/.nexus/relay/v1): the relay's own VERDICT on its peers
// (state/peers.json, written every 10 s by its daemon with the same signals as its wake-ups), and the messages sent to
// Claude not read yet. Shown only while the relay is in use: a message within the hour, or a message waiting.
const RECENT_MS = 60 * 60 * 1000
const FRESH_MS = 60 * 1000
async function readRelay($: EngineInterface): Promise<Relais | null> {
  const home = $.plugin.root.replaceAll('\\', '/').split('/.claude/')[0] ?? ''
  const root = `${home}/.nexus/relay/v1`
  if (!(await $.fs.exists(root))) return null
  let nonLus = 0
  let latest = 0
  try {
    const read_ = new Set((await $.fs.list(`${root}/read/claude`).catch(() => [])).map(e => e.name))
    for (const e of await $.fs.list(`${root}/messages`)) {
      const [id, , to, topic] = e.name.split('.')
      if (!id || topic === 'selftest') continue // self-test pongs do not count
      const m = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})/.exec(id)
      if (m) latest = Math.max(latest, Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]))
      if (to === 'claude' && !read_.has(id)) nonLus++
    }
  } catch { /* no mailbox */ }
  if (!nonLus && Date.now() - latest > RECENT_MS) return null // relay unused: nothing to show
  let verdict: any = null
  try { verdict = JSON.parse(await $.fs.read(`${root}/state/peers.json`)) } catch { /* daemon not there yet */ }
  const fresh = verdict?.at && Date.now() - new Date(verdict.at).getTime() < FRESH_MS
  const ok = (peer: string): boolean | null => (fresh ? Boolean(verdict?.[peer]?.ok) : null) // grey: silent daemon
  return { agy: ok('antigravity'), codex: ok('codex'), nonLus }
}

export const register: Register = on => {
  // A single on('session.start') in the whole module (otherwise the app does not load it): usage is chained there.
  on('session.start', async ($, e, next) => {
    const done = await next(e)
    await quotaAtStart($)
    $.ui.status(undefined) // clears an older version's status line: everything goes through the line under the prompt
    // One redraw per minute: the relay state moves on even without an answer.
    await update($, relais, () => null)
    const tick = async () => {
      const r = await readRelay($)
      await update($, relais, () => r)
      await update($, minute, n => n + 1)
    }
    await tick()
    $.clock.every(60000, tick)
    return done
  })

  on('session.measure', async ($, e, next) => {
    await quotaMeasure($, e)
    return next(e)
  })

  // Under the prompt, in the mode hints slot (about 25 characters, the app's limit): Nexus Relay peers reachable or not.
  on('ui.render', { component: 'SessionMode' }, async ($, e, next) => {
    const r = await read($, relais)
    await read($, minute) // redraw every minute
    if (!r || e.surface !== 'desktop') return next(e)
    const { Text } = $.ui.resolve(e)
    const dot = (ok: boolean | null) => (
      <Text color={ok === null ? undefined : ok ? '#30a46c' : '#e5484d'} dimColor={ok === null}>●</Text>
    )
    return (
      <Text wrap="truncate-end">
        {e.props.modes.length ? <Text dimColor>{`${e.props.modes.join(' & ')}  `}</Text> : null}
        {r ? (
          <Text>
            <Text dimColor>{'Codex '}</Text>
            {dot(r.codex)}
            <Text dimColor>{'  AGY '}</Text>
            {dot(r.agy)}
            {r.nonLus ? <Text color="#f5a524">{`  ✉${r.nonLus}`}</Text> : null}
          </Text>
        ) : null}
      </Text>
    )
  })
}
