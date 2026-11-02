import type { Register } from 'claude-code'

import { quotaAtStart, quotaMeasure } from './quota'

// NexusKeypad in Claude, one plugin: the exact usage for the pad, written by quota.ts. Nothing drawn in the app, no
// alert bubble, no network, no program started.
export const register: Register = on => {
  // A single on('session.start') in the whole module (otherwise the app does not load it): usage is chained there.
  on('session.start', async ($, e, next) => {
    const done = await next(e)
    await quotaAtStart($)
    $.ui.status(undefined) // clears an older version's status line
    return done
  })

  on('session.measure', async ($, e, next) => {
    await quotaMeasure($, e)
    return next(e)
  })
}
