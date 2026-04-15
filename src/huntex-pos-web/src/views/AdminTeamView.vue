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
    <McPageHeader title="Team & sales logins">
      <template #default>
        Sales staff use POS and stocktake with tighter rules. Only Owner or Dev can create Admin users; only Dev can
        create Owner.
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard title="Invite user">
      <p class="team-lead mc-text-muted">
        An email is sent so they can set their own password.
      </p>
      <div class="team-invite-grid">
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
      <div class="team-invite-actions">
        <McButton variant="primary" type="button" :disabled="busy" @click="createUser">Create user</McButton>
      </div>
    </McCard>

    <McCard title="Users" :padded="false">
      <div class="team-table-scroll">
        <table class="mc-table team-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Name</th>
              <th>Roles</th>
              <th>Status</th>
              <th class="team-th-actions">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in users" :key="u.id">
              <td class="team-cell-strong">{{ u.email }}</td>
              <td>{{ u.displayName ?? '—' }}</td>
              <td class="team-roles">{{ u.roles.join(', ') }}</td>
              <td>
                <McBadge :variant="u.lockedOut ? 'danger' : 'success'">
                  {{ u.lockedOut ? 'Locked' : 'Active' }}
                </McBadge>
              </td>
              <td class="team-actions">
                <div class="team-actions__inner">
                  <McButton variant="secondary" dense type="button" @click="toggleLock(u)">
                    {{ u.lockedOut ? 'Unlock' : 'Lock' }}
                  </McButton>
                  <McButton variant="secondary" dense type="button" @click="resendInvite(u)">Resend invite</McButton>
                  <McButton variant="ghost" dense type="button" @click="openReset(u)">Set password</McButton>
                </div>
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
      <p class="team-modal-hint mc-text-muted">Min. 10 characters with upper, lower, digit, and symbol.</p>
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
  max-width: 100%;
  overflow-x: clip;
}

.team-lead {
  margin: 0 0 1rem;
  font-size: 0.875rem;
  line-height: 1.45;
  max-width: 62ch;
}

.team-invite-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0 1.25rem;
  margin-bottom: 0.25rem;
}

@media (min-width: 720px) {
  .team-invite-grid {
    grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 0.9fr);
    align-items: end;
  }
}

.team-invite-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 1.25rem;
  padding-top: 1.25rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
}

.team-table-scroll {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  padding: 0 24px 16px;
}

.team-table {
  width: 100%;
  min-width: 600px;
}

.team-th-actions {
  text-align: right;
  width: 1%;
  white-space: nowrap;
}

.team-cell-strong {
  font-weight: 600;
}

.team-roles {
  font-size: 0.875rem;
}

.team-actions {
  text-align: right;
  vertical-align: middle;
}

.team-actions__inner {
  display: inline-flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.35rem;
  max-width: 22rem;
}

.team-modal-hint {
  margin: 0.5rem 0 0;
  font-size: 0.8125rem;
  line-height: 1.4;
}
</style>
