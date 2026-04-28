import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { http } from '@/api/http'
import { migrateLocalStorageKey } from '@/utils/storageMigrate'

const TOKEN_KEY = 'pos_token'
const LEGACY_TOKEN_KEY = 'huntex_token'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(migrateLocalStorageKey(TOKEN_KEY, LEGACY_TOKEN_KEY))
  const roles = ref<string[]>([])
  const email = ref<string | null>(null)
  const supplierId = ref<string | null>(null)

  const isAuthenticated = computed(() => !!token.value)
  const hasVendorScope = computed(() => !!supplierId.value)

  function setSession(t: string, r: string[], em?: string | null) {
    token.value = t
    roles.value = r
    email.value = em ?? null
    localStorage.setItem(TOKEN_KEY, t)
  }

  function clear() {
    token.value = null
    roles.value = []
    email.value = null
    supplierId.value = null
    localStorage.removeItem(TOKEN_KEY)
  }

  async function login(emailVal: string, password: string) {
    const { data } = await http.post('/api/auth/login', { email: emailVal, password })
    setSession(data.token, data.roles ?? [], emailVal)
    await loadMe()
  }

  async function loadMe() {
    if (!token.value) return
    try {
      const { data } = await http.get('/api/auth/me')
      roles.value = data.roles ?? []
      email.value = data.email ?? null
      supplierId.value = data.supplierId ?? null
    } catch {
      clear()
    }
  }

  function hasRole(...need: string[]) {
    return need.some((r) => roles.value.includes(r))
  }

  return { token, roles, email, supplierId, isAuthenticated, hasVendorScope, login, clear, loadMe, hasRole, setSession }
})
