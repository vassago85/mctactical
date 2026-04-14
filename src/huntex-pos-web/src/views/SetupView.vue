<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'

type MailDto = {
  domain: string
  from: string
  baseUrl: string
  attachPdf: boolean
  hasApiKey: boolean
}

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
    await load()
  } catch {
    err.value = 'Save failed'
  }
}
</script>

<template>
  <h1>Email setup (Mailgun)</h1>
  <p style="color: var(--mc-muted); font-size: 0.9rem; max-width: 42rem">
    Used for invoice emails. Values are stored in the database; environment variables still apply as fallback when a
    field is left empty here (except the API key — set it below or via
    <code>Mailgun__ApiKey</code>).
  </p>
  <p class="err" v-if="err">{{ err }}</p>
  <p v-if="ok" style="color: #a5d6a7">{{ ok }}</p>
  <div class="card">
    <div class="field">
      <label>Sending domain (Mailgun)</label>
      <input v-model="dto.domain" type="text" autocomplete="off" placeholder="mg.example.com" />
    </div>
    <div class="field">
      <label>From header</label>
      <input
        v-model="dto.from"
        type="text"
        autocomplete="off"
        placeholder="MC Tactical POS &lt;noreply@example.com&gt;"
      />
    </div>
    <div class="field">
      <label>API base URL</label>
      <input v-model="dto.baseUrl" type="text" autocomplete="off" />
    </div>
    <div class="field">
      <label>Private API key</label>
      <input v-model="apiKey" type="password" autocomplete="new-password" placeholder="Leave blank to keep current" />
      <p v-if="dto.hasApiKey" style="font-size: 0.85rem; color: var(--mc-muted); margin: 0.35rem 0 0">
        A key is configured (saved or environment). Enter a new key only to replace it.
      </p>
    </div>
    <label><input type="checkbox" v-model="dto.attachPdf" /> Attach PDF to invoice emails</label>
    <div style="margin-top: 1rem">
      <button type="button" class="btn" @click="save">Save</button>
    </div>
  </div>
</template>
