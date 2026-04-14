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
    }
    return Promise.reject(err)
  }
)
