import axios from 'axios'
import { useAuthStore } from '@/stores/auth'

const base = import.meta.env.VITE_API_BASE?.replace(/\/$/, '') || ''

export const http = axios.create({
  baseURL: base || undefined
})

http.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.token) {
    config.headers = config.headers ?? {}
    config.headers.Authorization = `Bearer ${auth.token}`
  }
  return config
})

http.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401) {
      const auth = useAuthStore()
      auth.clear()
      // Send the operator to login instead of leaving a half-dead screen behind.
      // Navigating by hash rather than importing the router avoids an
      // http -> router -> auth store -> http import cycle.
      const url: string = err.config?.url ?? ''
      const current = window.location.hash.replace(/^#/, '')
      if (!url.includes('/api/auth/') && !current.startsWith('/login')) {
        const redirect = current ? `?redirect=${encodeURIComponent(current)}` : ''
        window.location.hash = `#/login${redirect}`
      }
    }
    return Promise.reject(err)
  }
)
