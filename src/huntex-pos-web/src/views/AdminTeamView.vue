<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

type UserRow = {
  id: string
  email: string | null
  displayName: string | null
  roles: string[]
  lockedOut: boolean
}

const auth = useAuthStore()
const users = ref<UserRow[]>([])
const err = ref<string | null>(null)
const ok = ref<string | null>(null)
const busy = ref(false)

const email = ref('')
const password = ref('')
const displayName = ref('')
const role = ref<'Sales' | 'Admin' | 'Owner'>('Sales')

const canCreateAdmin = ref(false)
const canCreateOwner = ref(false)

const resetUserId = ref<string | null>(null)
const resetPassword = ref('')

onMounted(async () => {
  canCreateAdmin.value = auth.hasRole('Owner', 'Dev')
  canCreateOwner.value = auth.hasRole('Dev')
  await load()
})

async function load() {
  err.value = null
  try {
    const { data } = await http.get<UserRow[]>('/api/admin/users')
    users.value = data
  } catch {
    err.value = 'Could not load users'
  }
}

async function createUser() {
  err.value = null
  ok.value = null
  busy.value = true
  try {
    await http.post('/api/admin/users', {
      email: email.value.trim(),
      password: password.value,
      displayName: displayName.value.trim() || null,
      role: role.value
    })
    ok.value = 'User created'
    email.value = ''
    password.value = ''
    displayName.value = ''
    role.value = 'Sales'
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { errors?: string[]; error?: string } } }
    const list = ax.response?.data?.errors
    err.value = list?.length ? list.join(' ') : ax.response?.data?.error ?? 'Create failed'
  } finally {
    busy.value = false
  }
}

async function toggleLock(u: UserRow) {
  err.value = null
  try {
    await http.post(`/api/admin/users/${u.id}/lock`, { locked: !u.lockedOut })
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Update failed'
  }
}

async function applyReset() {
  if (!resetUserId.value || !resetPassword.value) return
  err.value = null
  try {
    await http.post(`/api/admin/users/${resetUserId.value}/password`, { newPassword: resetPassword.value })
    resetUserId.value = null
    resetPassword.value = ''
    ok.value = 'Password updated'
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { errors?: string[] } } }
    err.value = ax.response?.data?.errors?.join(' ') ?? 'Reset failed'
  }
}
</script>

<template>
  <h1>Team &amp; sales logins</h1>
  <p style="color: var(--mc-muted); font-size: 0.9rem">
    Add cashiers with the <strong>Sales</strong> role. They get shorter sessions and POS discount limits. Only Owner / Dev may create
    <strong>Admin</strong> users.
  </p>
  <p class="err" v-if="err">{{ err }}</p>
  <p v-if="ok" style="color: #a5d6a7">{{ ok }}</p>

  <div class="card">
    <h2>Add user</h2>
    <div class="field">
      <label>Email (login)</label>
      <input v-model="email" type="email" autocomplete="off" required />
    </div>
    <div class="field">
      <label>Password</label>
      <input v-model="password" type="password" autocomplete="new-password" required minlength="10" />
    </div>
    <p style="font-size: 0.8rem; color: var(--mc-muted)">
      At least 10 characters with upper, lower, digit, and a symbol (2026 baseline).
    </p>
    <div class="field">
      <label>Display name</label>
      <input v-model="displayName" autocomplete="off" />
    </div>
    <div class="field">
      <label>Role</label>
      <select v-model="role">
        <option value="Sales">Sales (POS / stocktake)</option>
        <option v-if="canCreateAdmin" value="Admin">Admin (import, reports, this screen)</option>
        <option v-if="canCreateOwner" value="Owner">Owner (full access, settings, team)</option>
      </select>
    </div>
    <button type="button" class="btn" :disabled="busy" @click="createUser">Create user</button>
  </div>

  <div class="card">
    <h2>Users</h2>
    <table>
      <thead>
        <tr>
          <th>Email</th>
          <th>Name</th>
          <th>Roles</th>
          <th>Status</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="u in users" :key="u.id">
          <td>{{ u.email }}</td>
          <td>{{ u.displayName }}</td>
          <td>{{ u.roles.join(', ') }}</td>
          <td>{{ u.lockedOut ? 'Locked' : 'Active' }}</td>
          <td class="row" style="gap: 0.35rem">
            <button type="button" class="btn secondary" @click="toggleLock(u)">
              {{ u.lockedOut ? 'Unlock' : 'Lock' }}
            </button>
            <button type="button" class="btn secondary" @click="resetUserId = u.id">Set password…</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>

  <div v-if="resetUserId" class="card">
    <h3>New password for user</h3>
    <div class="field">
      <label>New password</label>
      <input v-model="resetPassword" type="password" minlength="10" autocomplete="new-password" />
    </div>
    <div class="row">
      <button type="button" class="btn" @click="applyReset">Save password</button>
      <button type="button" class="btn secondary" @click="(resetUserId = null), (resetPassword = '')">Cancel</button>
    </div>
  </div>
</template>
