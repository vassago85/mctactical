import { ref, computed, readonly } from 'vue'
import axios from 'axios'

export interface BrandingTerminology {
  quote: string
  invoice: string
  customer: string
}

export interface BrandingFeatures {
  quotes: boolean
  discounts: boolean
  brandPricingRules: boolean
  accounts: boolean
}

export interface Branding {
  businessName: string
  logoUrl: string | null
  faviconUrl: string | null
  primaryColor: string
  secondaryColor: string
  accentColor: string
  terminology: BrandingTerminology
  features: BrandingFeatures
}

const DEFAULTS: Branding = {
  businessName: 'POS',
  logoUrl: null,
  faviconUrl: null,
  primaryColor: '',
  secondaryColor: '',
  accentColor: '',
  terminology: { quote: 'Quote', invoice: 'Invoice', customer: 'Customer' },
  features: { quotes: true, discounts: true, brandPricingRules: true, accounts: false },
}

const STORAGE_KEY = 'mc-branding-cache-v1'
const STALE_MS = 60 * 60 * 1000

interface CachedPayload { at: number; data: Branding }

const state = ref<Branding>({ ...DEFAULTS })
let loaded = false

function toPayload(raw: unknown): Branding {
  const r = (raw ?? {}) as Partial<Record<string, unknown>>
  const term = (r.terminology ?? {}) as Partial<Record<string, unknown>>
  const feat = (r.features ?? {}) as Partial<Record<string, unknown>>
  const stringOr = (v: unknown, d: string) => (typeof v === 'string' && v.length ? v : d)
  const boolOr = (v: unknown, d: boolean) => (typeof v === 'boolean' ? v : d)
  return {
    businessName: stringOr(r.businessName, DEFAULTS.businessName),
    logoUrl: typeof r.logoUrl === 'string' && r.logoUrl.length ? r.logoUrl : null,
    faviconUrl: typeof r.faviconUrl === 'string' && r.faviconUrl.length ? r.faviconUrl : null,
    primaryColor: stringOr(r.primaryColor, ''),
    secondaryColor: stringOr(r.secondaryColor, ''),
    accentColor: stringOr(r.accentColor, ''),
    terminology: {
      quote: stringOr(term.quote, DEFAULTS.terminology.quote),
      invoice: stringOr(term.invoice, DEFAULTS.terminology.invoice),
      customer: stringOr(term.customer, DEFAULTS.terminology.customer),
    },
    features: {
      quotes: boolOr(feat.quotes, true),
      discounts: boolOr(feat.discounts, true),
      brandPricingRules: boolOr(feat.brandPricingRules, true),
      accounts: boolOr(feat.accounts, false),
    },
  }
}

function applyCssVariables(b: Branding) {
  const root = document.documentElement
  if (b.accentColor) {
    root.style.setProperty('--mc-accent', b.accentColor)
    root.style.setProperty('--mc-focus', b.accentColor)
  } else {
    root.style.removeProperty('--mc-accent')
    root.style.removeProperty('--mc-focus')
  }
  if (b.primaryColor) root.style.setProperty('--mc-brand-primary', b.primaryColor)
  else root.style.removeProperty('--mc-brand-primary')
  if (b.secondaryColor) root.style.setProperty('--mc-brand-secondary', b.secondaryColor)
  else root.style.removeProperty('--mc-brand-secondary')

  if (b.businessName && typeof document !== 'undefined') {
    document.title = `${b.businessName} POS`
  }
  if (b.faviconUrl && typeof document !== 'undefined') {
    let link = document.querySelector("link[rel='icon']") as HTMLLinkElement | null
    if (!link) {
      link = document.createElement('link')
      link.rel = 'icon'
      document.head.appendChild(link)
    }
    link.href = b.faviconUrl
  }
}

function readCache(): Branding | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as CachedPayload
    if (!parsed?.data) return null
    return parsed.data
  } catch {
    return null
  }
}

function writeCache(b: Branding) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ at: Date.now(), data: b } satisfies CachedPayload))
  } catch {
    /* storage full / private mode — ignore */
  }
}

function isStale(): boolean {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return true
    const parsed = JSON.parse(raw) as CachedPayload
    return Date.now() - (parsed.at ?? 0) > STALE_MS
  } catch {
    return true
  }
}

async function fetchRemote(): Promise<Branding> {
  const base = import.meta.env.VITE_API_BASE?.replace(/\/$/, '') || ''
  const { data } = await axios.get(`${base}/api/settings/branding`)
  const b = toPayload(data)
  writeCache(b)
  return b
}

export async function initBranding(): Promise<void> {
  const cached = readCache()
  if (cached) {
    state.value = cached
    applyCssVariables(cached)
    loaded = true
    if (isStale()) {
      void fetchRemote()
        .then((b) => {
          state.value = b
          applyCssVariables(b)
        })
        .catch(() => {})
    }
    return
  }
  try {
    const b = await fetchRemote()
    state.value = b
    applyCssVariables(b)
  } catch {
    state.value = { ...DEFAULTS }
    applyCssVariables(state.value)
  } finally {
    loaded = true
  }
}

export async function refreshBranding(): Promise<void> {
  try {
    const b = await fetchRemote()
    state.value = b
    applyCssVariables(b)
  } catch { /* keep current */ }
}

export function useBranding() {
  return {
    branding: readonly(state),
    businessName: computed(() => state.value.businessName),
    logoUrl: computed(() => state.value.logoUrl),
    terminology: computed(() => state.value.terminology),
    features: computed(() => state.value.features),
    accentColor: computed(() => state.value.accentColor),
    isLoaded: () => loaded,
    refresh: refreshBranding,
  }
}
