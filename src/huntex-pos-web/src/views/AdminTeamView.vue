<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
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
  supplierId: string | null
  supplierName: string | null
}

type SupplierOption = { id: string; name: string }

const auth = useAuthStore()
const toast = useToast()
const users = ref<UserRow[]>([])
const suppliers = ref<SupplierOption[]>([])
const err = ref<string | null>(null)
const busy = ref(false)

const email = ref('')
const displayName = ref('')
const role = ref<'Sales' | 'Admin' | 'Owner'>('Sales')
const inviteSupplierId = ref<string>('')

const canCreateAdmin = ref(false)
const canCreateOwner = ref(false)

const showResetModal = ref(false)
const resetUserId = ref<string | null>(null)
const resetPassword = ref('')

const showDeleteModal = ref(false)
const deleteTarget = ref<UserRow | null>(null)
const deleteConfirmText = ref('')
const deleteBusy = ref(false)
const canDelete = ref(false)

const showEditModal = ref(false)
const editTarget = ref<UserRow | null>(null)
const editDisplayName = ref('')
const editRoles = ref<Record<string, boolean>>({ Sales: false, Admin: false, Owner: false, Dev: false })
const editBusy = ref(false)

onMounted(async () => {
  canCreateAdmin.value = auth.hasRole('Owner', 'Dev')
  canCreateOwner.value = auth.hasRole('Owner', 'Dev')
  canDelete.value = auth.hasRole('Owner', 'Dev')
  await Promise.all([load(), loadSuppliers()])
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

async function loadSuppliers() {
  try {
    const { data } = await http.get<SupplierOption[]>('/api/suppliers')
    suppliers.value = data
  } catch {
    // non-critical; the supplier dropdown just stays empty
  }
}

async function setSupplier(u: UserRow, supplierId: string) {
  err.value = null
  try {
    await http.post(`/api/admin/users/${u.id}/supplier`, {
      supplierId: supplierId || null
    })
    toast.success(supplierId ? 'Vendor link saved' : 'Vendor link removed')
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string; errors?: string[] } } }
    const msg = ax.response?.data?.errors?.join(' ') ?? ax.response?.data?.error ?? 'Could not update vendor link'
    err.value = msg
    toast.error(msg)
  }
}

async function createUser() {
  err.value = null
  busy.value = true
  try {
    await http.post('/api/admin/users', {
      email: email.value.trim(),
      displayName: displayName.value.trim() || null,
      role: role.value,
      supplierId: inviteSupplierId.value || null
    })
    toast.success('User created — setup email sent')
    email.value = ''
    displayName.value = ''
    role.value = 'Sales'
    inviteSupplierId.value = ''
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

function openDelete(u: UserRow) {
  deleteTarget.value = u
  deleteConfirmText.value = ''
  showDeleteModal.value = true
}

function closeDelete() {
  showDeleteModal.value = false
  deleteTarget.value = null
  deleteConfirmText.value = ''
}

async function confirmDelete() {
  if (!deleteTarget.value) return
  err.value = null
  deleteBusy.value = true
  try {
    await http.delete(`/api/admin/users/${deleteTarget.value.id}`)
    toast.success(`Deleted ${deleteTarget.value.email}`)
    closeDelete()
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string; errors?: string[] } } }
    const msg = ax.response?.data?.errors?.join(' ') ?? ax.response?.data?.error ?? 'Delete failed'
    err.value = msg
    toast.error(msg)
  } finally {
    deleteBusy.value = false
  }
}

function openEdit(u: UserRow) {
  editTarget.value = u
  editDisplayName.value = u.displayName ?? ''
  editRoles.value = {
    Sales: u.roles.includes('Sales'),
    Admin: u.roles.includes('Admin'),
    Owner: u.roles.includes('Owner'),
    Dev: u.roles.includes('Dev')
  }
  showEditModal.value = true
}

function closeEdit() {
  showEditModal.value = false
  editTarget.value = null
}

const editRoleList = computed(() =>
  Object.entries(editRoles.value)
    .filter(([, on]) => on)
    .map(([r]) => r)
)
const editCanSave = computed(() => !!editTarget.value && editRoleList.value.length > 0)

async function saveEdit() {
  if (!editTarget.value || !editCanSave.value) return
  err.value = null
  editBusy.value = true
  try {
    await http.put(`/api/admin/users/${editTarget.value.id}`, {
      displayName: editDisplayName.value.trim() || null,
      roles: editRoleList.value
    })
    toast.success('User updated')
    closeEdit()
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string; errors?: string[] } } }
    const msg = ax.response?.data?.errors?.join(' ') ?? ax.response?.data?.error ?? 'Update failed'
    err.value = msg
    toast.error(msg)
  } finally {
    editBusy.value = false
  }
}
</script>

<template>
  <div class="team-page">
    <McPageHeader title="Team & sales logins">
      <template #default>
        Sales staff use POS and stocktake with tighter rules. Only Owner or Dev can create Admin or Owner users.
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
        <McField label="Vendor scope (optional)" for-id="team-supplier" hint="Links a Sales user to a supplier. They'll see a report scoped to that supplier's stock & sales only.">
          <select id="team-supplier" v-model="inviteSupplierId">
            <option value="">— None —</option>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
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
              <th>Vendor scope</th>
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
                <select
                  class="team-supplier-select"
                  :value="u.supplierId ?? ''"
                  @change="setSupplier(u, ($event.target as HTMLSelectElement).value)"
                >
                  <option value="">— None —</option>
                  <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
                </select>
              </td>
              <td>
                <McBadge :variant="u.lockedOut ? 'danger' : 'success'">
                  {{ u.lockedOut ? 'Locked' : 'Active' }}
                </McBadge>
              </td>
              <td class="team-actions">
                <div class="team-actions__inner">
                  <McButton variant="secondary" dense type="button" @click="openEdit(u)">Edit</McButton>
                  <McButton variant="secondary" dense type="button" @click="toggleLock(u)">
                    {{ u.lockedOut ? 'Unlock' : 'Lock' }}
                  </McButton>
                  <McButton variant="secondary" dense type="button" @click="resendInvite(u)">Resend invite</McButton>
                  <McButton variant="ghost" dense type="button" @click="openReset(u)">Set password</McButton>
                  <McButton
                    v-if="canDelete && u.email !== auth.email && (!u.roles.includes('Owner') || auth.hasRole('Dev'))"
                    variant="danger"
                    dense
                    type="button"
                    @click="openDelete(u)"
                  >Delete</McButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McModal v-model="showEditModal" title="Edit user">
      <p class="team-modal-hint mc-text-muted" style="margin-bottom: 0.75rem">
        <strong>{{ editTarget?.email }}</strong>
      </p>
      <McField label="Display name" for-id="team-edit-name">
        <input id="team-edit-name" v-model="editDisplayName" type="text" autocomplete="off" />
      </McField>
      <McField label="Roles">
        <div class="team-edit-roles">
          <label class="team-edit-role">
            <input type="checkbox" v-model="editRoles.Sales" />
            <span><strong>Sales</strong> — POS / stocktake</span>
          </label>
          <label class="team-edit-role" :class="{ 'team-edit-role--disabled': !canCreateAdmin }">
            <input type="checkbox" v-model="editRoles.Admin" :disabled="!canCreateAdmin" />
            <span><strong>Admin</strong> — import, reports, team</span>
          </label>
          <label class="team-edit-role" :class="{ 'team-edit-role--disabled': !canCreateOwner }">
            <input type="checkbox" v-model="editRoles.Owner" :disabled="!canCreateOwner" />
            <span><strong>Owner</strong> — full access</span>
          </label>
          <label v-if="auth.hasRole('Dev')" class="team-edit-role">
            <input type="checkbox" v-model="editRoles.Dev" />
            <span><strong>Dev</strong> — developer tools</span>
          </label>
        </div>
      </McField>
      <p v-if="editRoleList.length === 0" class="team-modal-hint mc-text-warn">
        Pick at least one role.
      </p>
      <template #footer>
        <McButton variant="secondary" type="button" :disabled="editBusy" @click="closeEdit">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="editBusy || !editCanSave" @click="saveEdit">
          {{ editBusy ? 'Saving…' : 'Save changes' }}
        </McButton>
      </template>
    </McModal>

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

    <McModal v-model="showDeleteModal" title="Delete user">
      <p class="team-modal-hint">
        This permanently removes
        <strong>{{ deleteTarget?.email }}</strong>
        and their login. Existing invoices, quotes and stocktakes they created remain, but the link to their account is lost.
      </p>
      <p class="team-modal-hint mc-text-muted">
        Type <strong>DELETE</strong> to confirm.
      </p>
      <McField label="Confirmation" for-id="team-delete-confirm">
        <input
          id="team-delete-confirm"
          v-model="deleteConfirmText"
          type="text"
          autocomplete="off"
          placeholder="DELETE"
        />
      </McField>
      <template #footer>
        <McButton variant="secondary" type="button" :disabled="deleteBusy" @click="closeDelete">Cancel</McButton>
        <McButton
          variant="danger"
          type="button"
          :disabled="deleteBusy || deleteConfirmText.trim() !== 'DELETE'"
          @click="confirmDelete"
        >
          {{ deleteBusy ? 'Deleting…' : 'Delete user' }}
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.team-page {
  min-height: 100%;
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

.team-supplier-select {
  min-width: 10rem;
  max-width: 14rem;
  font-size: 0.875rem;
  padding: 0.3rem 0.5rem;
}

.team-edit-roles {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.team-edit-role {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
  cursor: pointer;
}

.team-edit-role--disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.mc-text-warn {
  color: #a84a00;
}
</style>
