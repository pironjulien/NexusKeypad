export type Relais = { agy: boolean | null; codex: boolean | null; nonLus: number }

declare module 'claude-code' {
  interface PluginState {
    'nexus-keypad': { minute: number; relais: Relais | null }
  }
}
