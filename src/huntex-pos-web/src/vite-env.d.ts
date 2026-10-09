/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

/** Chromium-only install prompt event; not in TypeScript's DOM lib. */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>
}

/**
 * Roll for It die bundle (`/rfi/rfi-die3d.js`) attaches a global factory that renders
 * a 10-sided die inside the given host. We load it lazily when the till opens the
 * roll dialog, so it stays out of the main POS JS bundle.
 */
interface RFIDie3DInstance {
  show(face: number): void
  start(): void
  settle(face: number): Promise<void>
  stop(): void
  dispose(): void
}

interface Window {
  RFIDie3D?: {
    create(host: HTMLElement, options: {
      size?: number
      dieColor?: string
      numberColor?: string
      accentColor?: string
    }): RFIDie3DInstance
  }
}
