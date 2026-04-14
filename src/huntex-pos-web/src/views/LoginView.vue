<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { logoLight } from '@/branding'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const email = ref('')
const password = ref('')
const err = ref<string | null>(null)
const busy = ref(false)

async function submit() {
  err.value = null
  busy.value = true
  try {
    await auth.login(email.value, password.value)
    const r = (route.query.redirect as string) || '/pos'
    await router.replace(r)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Login failed'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="auth-layout">
    <div class="auth-panel">
      <div class="auth-panel__brand">
        <img class="auth-panel__logo" :src="logoLight" alt="MC Tactical" />
        <p class="auth-panel__tagline">Point of sale</p>
      </div>
      <h1 class="sr-only">Sign in to MC Tactical POS</h1>
      <p class="auth-panel__title">Sign in</p>
      <p class="auth-panel__sub">Staff access — use the email and password provided by your manager.</p>

      <McAlert v-if="err" variant="error">{{ err }}</McAlert>

      <form class="auth-form" @submit.prevent="submit">
        <McField label="Email" for-id="login-email">
          <input id="login-email" v-model="email" type="email" autocomplete="username" required />
        </McField>
        <McField label="Password" for-id="login-pass">
          <input id="login-pass" v-model="password" type="password" autocomplete="current-password" required />
        </McField>
        <McButton variant="primary" type="submit" block :disabled="busy">
          {{ busy ? 'Signing in…' : 'Sign in' }}
        </McButton>
      </form>
      <p class="auth-panel__foot">mctactical.co.za</p>
    </div>
  </div>
</template>

<style scoped>
.auth-layout {
  min-height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
  background:
    radial-gradient(ellipse 100% 80% at 50% -30%, rgba(244, 122, 32, 0.22), transparent),
    linear-gradient(165deg, #121214 0%, #0a0a0b 45%, #151518 100%);
}

.auth-panel {
  width: 100%;
  max-width: 420px;
  padding: 2rem 1.75rem 1.75rem;
  background: #fff;
  border-radius: 16px;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.35);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.auth-panel__brand {
  text-align: center;
  margin-bottom: 1.25rem;
}

.auth-panel__logo {
  max-width: min(260px, 75vw);
  height: auto;
}

.auth-panel__tagline {
  margin: 0.5rem 0 0;
  font-size: 0.65rem;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: #7a7874;
}

.auth-panel__title {
  margin: 0 0 0.35rem;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  text-align: center;
  color: #1a1a1c;
}

.auth-panel__sub {
  margin: 0 0 1.25rem;
  text-align: center;
  font-size: 0.9rem;
  color: #5c5a56;
  line-height: 1.45;
}

.auth-form {
  margin-top: 0.5rem;
}

.auth-panel__foot {
  margin: 1.5rem 0 0;
  text-align: center;
  font-size: 0.7rem;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: #9a9690;
}
</style>
