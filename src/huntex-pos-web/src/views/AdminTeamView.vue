<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McModal from '@/components/ui/McModal.vue'

type UserRow = {
  id: string
  email: string | null
  displayName: string | null
  roles: string[]
  lockedOut: boolean
}

const auth = useAuthStore()
const toast = useToast()
const users = ref<UserRow[]>([])
const err = ref<string | null>(null)
const busy = ref(false)

const email = ref('')
const displayName = ref('')
const role = ref<'Sales' | 'Admin' | 'Owner'>('Sales')

const canCreateAdmin = ref(false)
const canCreateOwner = ref(false)

const showResetModal = ref(false)
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
  busy.value = true
  try {
    await http.post('/api/admin/users', {
      email: email.value.trim(),
      displayName: displayName.value.trim() || null,
      role: role.value
    })
    toast.success('User created — setup email sent')
    email.value = ''
    displayName.value = ''
    role.value = 'Sales'
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { errors?: string[]; error?: string } } }
    const list = ax.response?.data?.errors
    err.value = list?.length ? list.join(' ') : ax.response?.data?.error ?? 'Create failed'
    toast.error(err.value)
  } finally {
    busy.value = false
  }
}

async function toggleLock(u: UserRow) {
  err.value = null
  try {
    await http.post(`/api/admin/users/${u.id}/lock`, { locked: !u.lockedOut })
    toast.success(u.lockedOut ? 'User unlocked' : 'User locked')
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Update failed'
    toast.error(err.value)
  }
}

async function resendInvite(u: UserRow) {
  err.value = null
  try {
    const { data } = await http.post<{ message?: string }>(`/api/admin/users/${u.id}/resend-invite`)
    toast.success(data.message || 'Invite resent')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not send invite'
    toast.error(err.value)
  }
}

function openReset(u: UserRow) {
  resetUserId.value = u.id
  resetPassword.value = ''
  showResetModal.value = true
}

async function applyReset() {
  if (!resetUserId.value || !resetPassword.value) return
  err.value = null
  try {
    await http.post(`/api/admin/users/${resetUserId.value}/password`, { newPassword: resetPassword.value })
    showResetModal.value = false
    resetUserId.value = null
    resetPassword.value = ''
    toast.success('Password updated')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { errors?: string[] } } }
    err.value = ax.response?.data?.errors?.join(' ') ?? 'Reset failed'
    toast.error(err.value)
  }
}
</script>

<template>
  <div class="team-page">
    <McPageHeader
      title="Team & sales logins"
      description="Sales staff use POS and stocktake with tighter rules. Only Owner or Dev can create Admin users; only Dev can create Owner."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard title="Invite user">
      <p class="team-hint">An email is sent so they can set their own password.</p>
      <div class="team-form-grid">
        <McField label="Email (login)" for-id="team-email">
          <input id="team-email" v-model="email" type="email" autocomplete="off" required />
        </McField>
        <McField label="Display name" for-id="team-name">
          <input id="team-name" v-model="displayName" autocomplete="off" />
        </McField>
        <McField label="Role" for-id="team-role">
          <select id="team-role" v-model="role">
            <option value="Sales">Sales — POS / stocktake</option>
            <option v-if="canCreateAdmin" value="Admin">Admin — import, reports, team</option>
            <option v-if="canCreateOwner" value="Owner">Owner — full access</option>
          </select>
        </McField>
      </div>
      <McButton variant="primary" type="button" :disabled="busy" @click="createUser">Create user</McButton>
    </McCard>

    <McCard title="Users">
      <div class="team-table-wrap">
        <table class="team-table">
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
              <td>{{ u.displayName ?? '—' }}</td>
              <td>{{ u.roles.join(', ') }}</td>
              <td>
                <McBadge :variant="u.lockedOut ? 'danger' : 'success'">
                  {{ u.lockedOut ? 'Locked' : 'Active' }}
                </McBadge>
              </td>
              <td class="team-actions">
                <McButton variant="secondary" type="button" @click="toggleLock(u)">
                  {{ u.lockedOut ? 'Unlock' : 'Lock' }}
                </McButton>
                <McButton variant="secondary" type="button" @click="resendInvite(u)">Resend invite</McButton>
                <McButton variant="ghost" type="button" @click="openReset(u)">Set password</McButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McModal v-model="showResetModal" title="Set password">
      <McField label="New password" for-id="team-reset-pw">
        <input id="team-reset-pw" v-model="resetPassword" type="password" minlength="10" autocomplete="new-password" />
      </McField>
      <p class="team-hint">Min. 10 characters with upper, lower, digit, and symbol.</p>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showResetModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="resetPassword.length < 10" @click="applyReset">
          Save
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.team-page {
  min-height: 100%;
}

.team-hint {
  margin: 0 0 1rem;
  font-size: 0.88rem;
  color: #7a7874;
}

.team-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 0 1rem;
  margin-bottom: 1rem;
}

.team-table-wrap {
  overflow-x: auto;
}

.team-table {
  min-width: 720px;
}

.team-actions {
  white-space: nowrap;
}

.team-actions :deep(.mc-btn) {
  min-height: 38px;
  margin: 0.15rem;
  padding: 0 0.6rem;
  font-size: 0.75rem;
}
</style>
