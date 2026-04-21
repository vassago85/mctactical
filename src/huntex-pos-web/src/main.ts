import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { registerSW } from 'virtual:pwa-register'
import App from './App.vue'
import router from './router'
import { initBranding } from '@/composables/useBranding'
import './style.css'

registerSW({ immediate: true })

void initBranding()

// Point the browser at the runtime, branded PWA manifest so each deployment can
// present its own name/icon/colour when installed. The static manifest in
// vite.config.ts is only used as a fallback when the API is unreachable.
try {
  const link = document.createElement('link')
  link.rel = 'manifest'
  link.href = '/api/settings/branding/manifest.webmanifest'
  document.head.appendChild(link)
} catch { /* ignore */ }

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')
