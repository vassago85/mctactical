<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { http } from '@/api/http'
import { logoLight } from '@/branding'

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
    ok.value = data.message || 'Password set! Redirecting to login…'
    setTimeout(() => router.replace('/login'), 2500)
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
  <div class="card" style="max-width: 420px; margin: 2rem auto">
    <div class="login-logo-wrap">
      <img class="login-logo" :src="logoLight" alt="MC Tactical" />
    </div>
    <h1 style="text-align: center; font-size: 1.25rem; margin-bottom: 0.5rem">Set your password</h1>
    <p style="text-align: center; color: var(--mc-muted); font-size: 0.9rem; margin-bottom: 1rem">
      Choose a secure password for <strong>{{ email }}</strong>
    </p>

    <p class="err" v-if="err">{{ err }}</p>
    <p v-if="ok" style="color: #a5d6a7; text-align: center">{{ ok }}</p>

    <form @submit.prevent="submit" v-if="!ok && token">
      <div class="field">
        <label>New password</label>
        <input v-model="password" type="password" autocomplete="new-password" required minlength="10" />
      </div>
      <p style="font-size: 0.8rem; color: var(--mc-muted); margin-top: -0.5rem">
        At least 10 characters with upper, lower, digit, and a symbol.
      </p>
      <div class="field">
        <label>Confirm password</label>
        <input v-model="confirm" type="password" autocomplete="new-password" required minlength="10" />
      </div>
      <button class="btn" type="submit" :disabled="busy" style="width: 100%">Set password &amp; continue</button>
    </form>
  </div>
</template>
