<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { logoLight } from '@/branding'

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
  <div class="card" style="max-width: 400px; margin: 2rem auto">
    <div class="login-logo-wrap">
      <img class="login-logo" :src="logoLight" alt="MC Tactical" />
    </div>
    <h1 class="sr-only">Sign in to MC Tactical POS</h1>
    <p class="tagline" style="margin-bottom: 1rem; text-align: center">Sign in to POS</p>
    <p class="err" v-if="err">{{ err }}</p>
    <form @submit.prevent="submit">
      <div class="field">
        <label>Email</label>
        <input v-model="email" type="email" autocomplete="username" required />
      </div>
      <div class="field">
        <label>Password</label>
        <input v-model="password" type="password" autocomplete="current-password" required />
      </div>
      <button class="btn" type="submit" :disabled="busy">Sign in</button>
    </form>
  </div>
</template>
