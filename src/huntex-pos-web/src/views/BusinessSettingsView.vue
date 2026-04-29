<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { useBranding } from '@/composables/useBranding'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McActionBar from '@/components/ui/McActionBar.vue'

interface BusinessSettings {
  businessName: string
  legalName: string
  vatNumber: string
  currency: string
  timeZone: string
  email: string
  phone: string
  address: string
  website: string
  websiteLabel: string
  logoUrl: string | null
  faviconUrl: string | null
  primaryColor: string
  secondaryColor: string
  accentColor: string
  receiptFooter: string
  quoteTerms: string
  invoiceTerms: string
  returnPolicy: string
  quoteLabel: string
  invoiceLabel: string
  customerLabel: string
  enableQuotes: boolean
  enableDiscounts: boolean
  enableBrandPricingRules: boolean
  accountsEnabled: boolean
}

const toast = useToast()
const { refresh: refreshBrandingState } = useBranding()

const loading = ref(true)
const busy = ref(false)
const err = ref<string | null>(null)
const form = ref<BusinessSettings>(emptySettings())

function emptySettings(): BusinessSettings {
  return {
    businessName: '',
    legalName: '',
    vatNumber: '',
    currency: 'ZAR',
    timeZone: 'Africa/Johannesburg',
    email: '',
    phone: '',
    address: '',
    website: '',
    websiteLabel: '',
    logoUrl: null,
    faviconUrl: null,
    primaryColor: '',
    secondaryColor: '',
    accentColor: '',
    receiptFooter: '',
    quoteTerms: '',
    invoiceTerms: '',
    returnPolicy: '',
    quoteLabel: 'Quote',
    invoiceLabel: 'Invoice',
    customerLabel: 'Customer',
    enableQuotes: true,
    enableDiscounts: true,
    enableBrandPricingRules: true,
    accountsEnabled: false
  }
}

async function load() {
  loading.value = true
  err.value = null
  try {
    const { data } = await http.get<BusinessSettings>('/api/settings/business')
    form.value = { ...emptySettings(), ...data }
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Failed to load business settings.'
  } finally {
    loading.value = false
  }
}

async function save() {
  busy.value = true
  err.value = null
  try {
    const { data } = await http.put<BusinessSettings>('/api/settings/business', form.value)
    form.value = { ...emptySettings(), ...data }
    await refreshBrandingState()
    toast.success('Business settings saved')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Failed to save settings.'
  } finally {
    busy.value = false
  }
}

const logoInput = ref<HTMLInputElement | null>(null)
const faviconInput = ref<HTMLInputElement | null>(null)

async function uploadLogo(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  await uploadAsset(file, 'logo')
  input.value = ''
}

async function uploadFavicon(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  await uploadAsset(file, 'favicon')
  input.value = ''
}

async function uploadAsset(file: File, kind: 'logo' | 'favicon') {
  busy.value = true
  try {
    const fd = new FormData()
    fd.append('file', file)
    const { data } = await http.post<{ url: string }>(`/api/settings/business/${kind}`, fd, {
      headers: { 'Content-Type': 'multipart/form-data' }
    })
    if (kind === 'logo') form.value.logoUrl = data.url + '?v=' + Date.now()
    else form.value.faviconUrl = data.url + '?v=' + Date.now()
    await refreshBrandingState()
    toast.success(`${kind === 'logo' ? 'Logo' : 'Favicon'} updated`)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? `Failed to upload ${kind}.`
  } finally {
    busy.value = false
  }
}

async function deleteLogo() {
  if (!confirm('Remove the uploaded logo? The default logo will be used.')) return
  busy.value = true
  try {
    await http.delete('/api/settings/business/logo')
    form.value.logoUrl = null
    await refreshBrandingState()
    toast.success('Logo removed')
  } finally { busy.value = false }
}

async function deleteFavicon() {
  if (!confirm('Remove the uploaded favicon?')) return
  busy.value = true
  try {
    await http.delete('/api/settings/business/favicon')
    form.value.faviconUrl = null
    await refreshBrandingState()
    toast.success('Favicon removed')
  } finally { busy.value = false }
}

const colorPreview = computed(() => ({
  '--preview-primary': form.value.primaryColor || '#0a0a0b',
  '--preview-secondary': form.value.secondaryColor || '#5c5a56',
  '--preview-accent': form.value.accentColor || '#f47a20'
}))

onMounted(load)
</script>

<template>
  <section class="page page--settings">
    <McPageHeader
      title="Business settings"
      description="Company details, branding, document templates, terminology, and feature toggles. Saved changes apply everywhere immediately."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div v-if="loading" class="bs-loading">Loading…</div>

    <template v-else>
      <!-- Company details -->
      <McCard title="Company details">
        <div class="bs-grid">
          <McField label="Business name" for-id="bs-name">
            <input id="bs-name" v-model="form.businessName" type="text" autocomplete="organization" />
          </McField>
          <McField label="Legal / registered name" for-id="bs-legal">
            <input id="bs-legal" v-model="form.legalName" type="text" />
          </McField>
          <McField label="VAT / tax number" for-id="bs-vat">
            <input id="bs-vat" v-model="form.vatNumber" type="text" />
          </McField>
          <McField label="Currency" for-id="bs-currency">
            <input id="bs-currency" v-model="form.currency" type="text" maxlength="3" />
          </McField>
          <McField label="Time zone" for-id="bs-tz">
            <input id="bs-tz" v-model="form.timeZone" type="text" />
          </McField>
          <McField label="Email" for-id="bs-email">
            <input id="bs-email" v-model="form.email" type="email" autocomplete="email" />
          </McField>
          <McField label="Phone" for-id="bs-phone">
            <input id="bs-phone" v-model="form.phone" type="tel" autocomplete="tel" />
          </McField>
          <McField label="Website" for-id="bs-site">
            <input id="bs-site" v-model="form.website" type="url" placeholder="https://" />
          </McField>
          <McField label="Website label" for-id="bs-site-label" hint="Shown on invoices; defaults to the URL's host.">
            <input id="bs-site-label" v-model="form.websiteLabel" type="text" />
          </McField>
          <McField class="bs-grid__full" label="Address" for-id="bs-addr">
            <textarea id="bs-addr" v-model="form.address" rows="3" />
          </McField>
        </div>
      </McCard>

      <!-- Branding -->
      <McCard title="Branding">
        <div class="bs-brand">
          <div class="bs-brand__block">
            <label class="bs-brand__label">Logo</label>
            <div class="bs-brand__preview bs-brand__preview--logo">
              <img v-if="form.logoUrl" :src="form.logoUrl" alt="Logo preview" />
              <span v-else class="bs-brand__empty">No logo uploaded</span>
            </div>
            <div class="bs-brand__actions">
              <input ref="logoInput" type="file" accept=".png,.jpg,.jpeg,.webp,.svg" hidden @change="uploadLogo" />
              <McButton variant="primary" type="button" :disabled="busy" @click="logoInput?.click()">Upload logo</McButton>
              <McButton v-if="form.logoUrl" variant="ghost" type="button" :disabled="busy" @click="deleteLogo">Remove</McButton>
            </div>
            <p class="bs-brand__hint">PNG, JPG, WebP, or SVG. Shown on the app shell, login screen, invoices and labels.</p>
          </div>

          <div class="bs-brand__block">
            <label class="bs-brand__label">Favicon</label>
            <div class="bs-brand__preview bs-brand__preview--favicon">
              <img v-if="form.faviconUrl" :src="form.faviconUrl" alt="Favicon preview" />
              <span v-else class="bs-brand__empty">Default</span>
            </div>
            <div class="bs-brand__actions">
              <input ref="faviconInput" type="file" accept=".png,.ico,.svg" hidden @change="uploadFavicon" />
              <McButton variant="primary" type="button" :disabled="busy" @click="faviconInput?.click()">Upload favicon</McButton>
              <McButton v-if="form.faviconUrl" variant="ghost" type="button" :disabled="busy" @click="deleteFavicon">Remove</McButton>
            </div>
            <p class="bs-brand__hint">Small icon used in browser tabs, bookmarks, and the installed app.</p>
          </div>
        </div>

        <div class="bs-colors" :style="colorPreview">
          <McField label="Primary colour" for-id="bs-c1" hint="Dark brand colour. Used in headers and key UI surfaces.">
            <div class="bs-color-input">
              <input id="bs-c1" v-model="form.primaryColor" type="text" placeholder="#0a0a0b" />
              <input type="color" v-model="form.primaryColor" aria-label="Pick primary colour" />
            </div>
          </McField>
          <McField label="Secondary colour" for-id="bs-c2" hint="Used for supporting text and subtle UI elements.">
            <div class="bs-color-input">
              <input id="bs-c2" v-model="form.secondaryColor" type="text" placeholder="#5c5a56" />
              <input type="color" v-model="form.secondaryColor" aria-label="Pick secondary colour" />
            </div>
          </McField>
          <McField label="Accent colour" for-id="bs-c3" hint="Call-to-action colour used on buttons, focus rings, invoices.">
            <div class="bs-color-input">
              <input id="bs-c3" v-model="form.accentColor" type="text" placeholder="#f47a20" />
              <input type="color" v-model="form.accentColor" aria-label="Pick accent colour" />
            </div>
          </McField>
        </div>

        <div class="bs-swatches">
          <span class="bs-swatch" :style="{ background: 'var(--preview-primary)' }">Primary</span>
          <span class="bs-swatch bs-swatch--dark" :style="{ background: 'var(--preview-secondary)' }">Secondary</span>
          <span class="bs-swatch" :style="{ background: 'var(--preview-accent)' }">Accent</span>
        </div>
      </McCard>

      <!-- Documents -->
      <McCard title="Document content">
        <div class="bs-grid">
          <McField class="bs-grid__full" label="Invoice terms" for-id="bs-it" hint="Shown at the bottom of invoices.">
            <textarea id="bs-it" v-model="form.invoiceTerms" rows="3" />
          </McField>
          <McField class="bs-grid__full" label="Quote terms" for-id="bs-qt" hint="Shown at the bottom of quotes.">
            <textarea id="bs-qt" v-model="form.quoteTerms" rows="3" />
          </McField>
          <McField class="bs-grid__full" label="Receipt footer" for-id="bs-rf" hint="Small print on printed receipts.">
            <textarea id="bs-rf" v-model="form.receiptFooter" rows="2" />
          </McField>
          <McField class="bs-grid__full" label="Return policy" for-id="bs-rp">
            <textarea id="bs-rp" v-model="form.returnPolicy" rows="3" />
          </McField>
        </div>
      </McCard>

      <!-- Terminology -->
      <McCard title="Terminology">
        <p class="bs-hint">Override the labels used for documents and customers — for example, "Estimate" instead of "Quote".</p>
        <div class="bs-grid">
          <McField label="Quote label" for-id="bs-ql">
            <input id="bs-ql" v-model="form.quoteLabel" type="text" />
          </McField>
          <McField label="Invoice label" for-id="bs-il">
            <input id="bs-il" v-model="form.invoiceLabel" type="text" />
          </McField>
          <McField label="Customer label" for-id="bs-cl">
            <input id="bs-cl" v-model="form.customerLabel" type="text" />
          </McField>
        </div>
      </McCard>

      <!-- Features -->
      <McCard title="Feature toggles">
        <div class="bs-toggles">
          <McCheckbox v-model="form.enableQuotes" label="Enable quotes" hint="Show the quotes section in navigation and allow creating quotes." />
          <McCheckbox v-model="form.enableDiscounts" label="Enable discounts" hint="Allow staff to apply promotions and specials at the POS." />
          <McCheckbox v-model="form.enableBrandPricingRules" label="Enable supplier / brand pricing rules" hint="Show pricing rule configuration in the pricing settings page." />
          <McCheckbox v-model="form.accountsEnabled" label="Enable customer accounts (AR)" hint="Allow charging sales to customer accounts. Existing cash, card and EFT sales are unaffected." />
        </div>
      </McCard>

      <McActionBar>
        <McButton variant="primary" type="button" :disabled="busy" @click="save">
          {{ busy ? 'Saving…' : 'Save business settings' }}
        </McButton>
      </McActionBar>
    </template>
  </section>
</template>

<style scoped>
.bs-loading {
  padding: 2rem;
  text-align: center;
  color: var(--mc-muted);
}

.bs-hint {
  margin: 0 0 0.75rem;
  color: var(--mc-muted);
  font-size: 0.875rem;
}

.bs-grid {
  display: grid;
  gap: 0.75rem 1rem;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}
.bs-grid__full {
  grid-column: 1 / -1;
}

.bs-brand {
  display: grid;
  gap: 1.5rem;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  margin-bottom: 1.25rem;
}

.bs-brand__block {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.bs-brand__label {
  font-weight: 600;
  font-size: 0.85rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-muted);
}

.bs-brand__preview {
  border: 1px dashed var(--mc-border);
  border-radius: 10px;
  background: #fafaf8;
  padding: 1rem;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 96px;
}

.bs-brand__preview--logo img {
  max-width: 100%;
  max-height: 96px;
}

.bs-brand__preview--favicon {
  min-height: 72px;
}
.bs-brand__preview--favicon img {
  width: 48px;
  height: 48px;
  object-fit: contain;
}

.bs-brand__empty {
  color: var(--mc-muted);
  font-size: 0.85rem;
}

.bs-brand__actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.bs-brand__hint {
  margin: 0;
  font-size: 0.8rem;
  color: var(--mc-muted);
  line-height: 1.4;
}

.bs-colors {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.bs-color-input {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
.bs-color-input input[type="text"] {
  flex: 1;
}
.bs-color-input input[type="color"] {
  width: 36px;
  height: 36px;
  border: 1px solid var(--mc-border);
  border-radius: 6px;
  cursor: pointer;
  padding: 2px;
}

.bs-swatches {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
  flex-wrap: wrap;
}
.bs-swatch {
  padding: 0.4rem 0.75rem;
  border-radius: 6px;
  color: #fff;
  font-size: 0.8rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  min-width: 90px;
  text-align: center;
}
.bs-swatch--dark {
  color: #fff;
}

.bs-toggles {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
</style>
