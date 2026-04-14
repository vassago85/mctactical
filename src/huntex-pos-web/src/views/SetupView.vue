<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'

type MailDto = {
  domain: string
  from: string
  baseUrl: string
  attachPdf: boolean
  hasApiKey: boolean
}

const toast = useToast()
const dto = ref<MailDto>({
  domain: '',
  from: '',
  baseUrl: 'https://api.mailgun.net/v3',
  attachPdf: false,
  hasApiKey: false
})
const apiKey = ref('')
const err = ref<string | null>(null)
const ok = ref<string | null>(null)
const testTo = ref('')
const testBusy = ref(false)

async function load() {
  const { data } = await http.get<MailDto>('/api/settings/mail')
  dto.value = { ...data }
  apiKey.value = ''
}

onMounted(() => void load().catch(() => (err.value = 'Load failed')))

async function save() {
  err.value = null
  ok.value = null
  try {
    await http.put('/api/settings/mail', {
      apiKey: apiKey.value.trim() || undefined,
      domain: dto.value.domain,
      from: dto.value.from,
      baseUrl: dto.value.baseUrl,
      attachPdf: dto.value.attachPdf
    })
    ok.value = 'Saved'
    toast.success('Mail settings saved')
    await load()
  } catch {
    err.value = 'Save failed'
    toast.error('Save failed')
  }
}

async function sendTest() {
  if (!testTo.value.trim()) return
  err.value = null
  ok.value = null
  testBusy.value = true
  try {
    const { data } = await http.post<{ message?: string }>('/api/settings/mail/test', {
      to: testTo.value.trim()
    })
    ok.value = data.message ?? 'Test email sent'
    toast.success(ok.value)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Test email failed'
    toast.error(err.value)
  } finally {
    testBusy.value = false
  }
}
</script>

<template>
  <div class="mail-page">
    <McPageHeader
      title="Email (Mailgun)"
      description="Invoice emails use these settings. Values are stored in the database; environment variables still apply as fallback. Set the API key here or via Mailgun__ApiKey."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>
    <McAlert v-if="ok" variant="success">{{ ok }}</McAlert>

    <McCard title="Sending configuration">
      <McField label="Sending domain" for-id="mail-domain" hint="e.g. mg.yourdomain.com">
        <input id="mail-domain" v-model="dto.domain" type="text" autocomplete="off" placeholder="mg.example.com" />
      </McField>
      <McField label="From header" for-id="mail-from">
        <input
          id="mail-from"
          v-model="dto.from"
          type="text"
          autocomplete="off"
          placeholder="MC Tactical POS &lt;noreply@example.com&gt;"
        />
      </McField>
      <McField label="API base URL" for-id="mail-base">
        <input id="mail-base" v-model="dto.baseUrl" type="text" autocomplete="off" />
      </McField>
      <McField label="Private API key" for-id="mail-key" hint="Leave blank to keep the current key.">
        <input id="mail-key" v-model="apiKey" type="password" autocomplete="new-password" placeholder="••••••••" />
      </McField>
      <p v-if="dto.hasApiKey" class="mail-key-note">A key is already configured (database or environment).</p>
      <label class="mail-check">
        <input v-model="dto.attachPdf" type="checkbox" />
        Attach PDF to invoice emails
      </label>
      <McButton variant="primary" type="button" @click="save">Save</McButton>
    </McCard>

    <McCard title="Send test email">
      <p class="mail-lead">Save settings first, then send a test to confirm DNS and credentials.</p>
      <McField label="Recipient email" for-id="mail-test">
        <input id="mail-test" v-model="testTo" type="email" placeholder="you@example.com" autocomplete="email" />
      </McField>
      <McButton variant="secondary" type="button" :disabled="testBusy || !testTo.trim()" @click="sendTest">
        {{ testBusy ? 'Sending…' : 'Send test' }}
      </McButton>
    </McCard>
  </div>
</template>

<style scoped>
.mail-page {
  min-height: 100%;
}

.mail-key-note {
  margin: -0.5rem 0 1rem;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #4a4842);
}

.mail-check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
  font-weight: 500;
  cursor: pointer;
}

.mail-lead {
  margin: 0 0 1rem;
  color: var(--mc-app-text-muted, #4a4842);
  font-size: 0.95rem;
}
</style>
