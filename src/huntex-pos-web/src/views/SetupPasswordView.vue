<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { http } from '@/api/http'
import { logoDark } from '@/branding'
import { useBranding } from '@/composables/useBranding'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'

const { businessName, logoUrl } = useBranding()

const route = useRoute()
const router = useRouter()

const email = ref('')
const token = ref('')
const password = ref('')
const confirm = ref('')
const err = ref<string | null>(null)
const ok = ref<string | null>(null)
const busy = ref(false)

onMounted(() => {
  email.value = (route.query.email as string) || ''
  token.value = (route.query.token as string) || ''
  if (!email.value || !token.value) {
    err.value = 'Invalid setup link. Please use the link from your email.'
  }
})

async function submit() {
  err.value = null
  ok.value = null

  if (password.value !== confirm.value) {
    err.value = 'Passwords do not match.'
    return
  }

  busy.value = true
  try {
    const { data } = await http.post('/api/auth/setup-password', {
      email: email.value,
      token: token.value,
      newPassword: password.value
    })
    ok.value = data.message || 'Password set successfully. Redirecting to sign in…'
    setTimeout(() => router.replace('/login'), 2800)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string; errors?: string[] } } }
    const list = ax.response?.data?.errors
    err.value = list?.length ? list.join(' ') : ax.response?.data?.error ?? 'Setup failed. The link may have expired.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="auth-layout">
    <div class="auth-panel">
      <div class="auth-panel__brand">
        <img class="auth-panel__logo" :src="logoUrl ?? logoDark" :alt="businessName" width="240" height="72" />
      </div>
      <p class="auth-panel__title">Set your password</p>
      <p class="auth-panel__sub">
        Secure account for <strong>{{ email || '…' }}</strong>
      </p>

      <McAlert v-if="err" variant="error">{{ err }}</McAlert>
      <McAlert v-if="ok" variant="success">{{ ok }}</McAlert>

      <form v-if="!ok && token" class="auth-form" @submit.prevent="submit">
        <McField label="New password" for-id="sp-pw">
          <input id="sp-pw" v-model="password" type="password" autocomplete="new-password" required minlength="10" />
        </McField>
        <p class="auth-hint">At least 10 characters with upper, lower, digit, and a symbol.</p>
        <McField label="Confirm password" for-id="sp-pw2">
          <input id="sp-pw2" v-model="confirm" type="password" autocomplete="new-password" required minlength="10" />
        </McField>
        <McButton variant="primary" type="submit" block :disabled="busy">
          {{ busy ? 'Saving…' : 'Set password & continue' }}
        </McButton>
      </form>
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
  max-width: 440px;
  padding: 2rem 1.75rem;
  background: #fff;
  border-radius: 16px;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.35);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.auth-panel__brand {
  text-align: center;
  margin-bottom: 1rem;
}

.auth-panel__logo {
  max-width: min(240px, 70vw);
  height: auto;
}

.auth-panel__title {
  margin: 0 0 0.5rem;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.35rem;
  font-weight: 700;
  letter-spacing: 0.05em;
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

.auth-hint {
  margin: -0.5rem 0 1rem;
  font-size: 0.8rem;
  color: #7a7874;
}
</style>
