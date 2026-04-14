<script setup lang="ts">
import { computed, ref } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McModal from '@/components/ui/McModal.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'

type Product = { id: string; sku: string; barcode?: string | null; name: string; qtyOnHand: number }
type Session = {
  id: string
  name: string
  status: string
  lines: Array<{
    id: string
    productId: string
    productName: string
    sku: string
    qtyBefore: number
    qtyCounted: number
  }>
}

const auth = useAuthStore()
const toast = useToast()
const canPost = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))

const sessionName = ref(`Count ${new Date().toLocaleDateString()}`)
const session = ref<Session | null>(null)
const q = ref('')
const results = ref<Product[]>([])
const countInput = ref(0)
const selectedProduct = ref<Product | null>(null)
const scanOpen = ref(false)
const err = ref<string | null>(null)
const busy = ref(false)
const showPostModal = ref(false)

const stepActive = computed(() => {
  if (!session.value) return 1
  if (session.value.status === 'Draft') return 2
  return 3
})

async function createSession() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.post<Session>('/api/stocktake/sessions', { name: sessionName.value })
    session.value = data
    toast.success('Session started — find a product to count.')
  } catch {
    err.value = 'Could not create session'
    toast.error('Could not create session')
  } finally {
    busy.value = false
  }
}

async function search() {
  const s = q.value.trim()
  if (!s) {
    results.value = []
    return
  }
  const { data } = await http.get<Product[]>('/api/products', { params: { q: s, take: 30 } })
  results.value = data
}

async function pickProduct(p: Product) {
  selectedProduct.value = p
  countInput.value = p.qtyOnHand
  q.value = p.name
}

function onScan(code: string) {
  q.value = code.trim()
  scanOpen.value = false
  void (async () => {
    await search()
    const hit =
      results.value.find((p) => p.barcode === code.trim() || p.sku === code.trim()) ?? results.value[0]
    if (hit) await pickProduct(hit)
  })()
}

async function saveLine() {
  if (!session.value || !selectedProduct.value) return
  err.value = null
  busy.value = true
  try {
    await http.post(`/api/stocktake/sessions/${session.value.id}/lines`, {
      productId: selectedProduct.value.id,
      qtyCounted: countInput.value
    })
    const { data } = await http.get<Session>(`/api/stocktake/sessions/${session.value.id}`)
    session.value = data
    toast.success('Line saved')
    selectedProduct.value = null
    q.value = ''
    results.value = []
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Save failed'
    toast.error(err.value)
  } finally {
    busy.value = false
  }
}

async function confirmPost() {
  if (!session.value) return
  busy.value = true
  err.value = null
  try {
    await http.post(`/api/stocktake/sessions/${session.value.id}/post`)
    const { data } = await http.get<Session>(`/api/stocktake/sessions/${session.value.id}`)
    session.value = data
    showPostModal.value = false
    toast.success('Stocktake posted — on-hand quantities were updated.')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Post failed'
    toast.error(err.value)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="st-page">
    <McPageHeader
      title="Stocktake"
      description="Create a session, count products, then post when ready. Posting updates stock and requires Admin, Owner, or Dev."
    />

    <nav class="st-steps" aria-label="Progress">
      <div class="st-step" :class="{ 'st-step--active': stepActive >= 1, 'st-step--current': stepActive === 1 }">
        <span class="st-step__n">1</span>
        <span class="st-step__l">Start</span>
      </div>
      <div class="st-step__line" :class="{ 'st-step__line--on': stepActive >= 2 }" />
      <div class="st-step" :class="{ 'st-step--active': stepActive >= 2, 'st-step--current': stepActive === 2 }">
        <span class="st-step__n">2</span>
        <span class="st-step__l">Count</span>
      </div>
      <div class="st-step__line" :class="{ 'st-step__line--on': stepActive >= 3 }" />
      <div class="st-step" :class="{ 'st-step--active': stepActive >= 3, 'st-step--current': stepActive === 3 }">
        <span class="st-step__n">3</span>
        <span class="st-step__l">Done</span>
      </div>
    </nav>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard v-if="!session" title="New session">
      <McField label="Session name" for-id="st-name">
        <input id="st-name" v-model="sessionName" type="text" />
      </McField>
      <McButton variant="primary" type="button" :disabled="busy" @click="createSession">Start session</McButton>
    </McCard>

    <template v-else>
      <div class="st-session-bar">
        <div>
          <strong>{{ session.name }}</strong>
          <McBadge :variant="session.status === 'Draft' ? 'accent' : 'success'" class="st-session-badge">
            {{ session.status }}
          </McBadge>
        </div>
        <p class="st-session-meta">{{ session.lines.length }} line(s) recorded</p>
      </div>

      <div class="st-grid">
        <McCard title="Find product">
          <div class="st-scan">
            <McButton variant="secondary" type="button" @click="scanOpen = !scanOpen">
              {{ scanOpen ? 'Hide scanner' : 'Scan barcode' }}
            </McButton>
          </div>
          <div v-if="scanOpen" class="st-scanner">
            <BarcodeScanner :active="scanOpen" @decode="onScan" />
          </div>
          <McField label="Search" for-id="st-q">
            <div class="st-search-row">
              <input
                id="st-q"
                v-model="q"
                type="search"
                placeholder="Name, SKU, barcode — words in any order"
                @keyup.enter="search"
              />
              <McButton variant="secondary" type="button" @click="search">Search</McButton>
            </div>
          </McField>
          <ul v-if="results.length" class="st-results">
            <li
              v-for="p in results"
              :key="p.id"
              class="st-result"
              role="button"
              tabindex="0"
              @click="pickProduct(p)"
              @keyup.enter="pickProduct(p)"
            >
              <span class="st-result__name">{{ p.name }}</span>
              <span class="st-result__sku">{{ p.sku }}</span>
              <span class="st-result__qty">Stock {{ p.qtyOnHand }}</span>
            </li>
          </ul>
          <McEmptyState
            v-else-if="q.trim()"
            title="No results"
            hint="Try another SKU, barcode, or name."
          />
        </McCard>

        <McCard v-if="selectedProduct" title="Record count">
          <p class="st-counting">
            <strong>{{ selectedProduct.name }}</strong>
            <span class="st-counting__sub">System qty before: {{ selectedProduct.qtyOnHand }}</span>
          </p>
          <McField label="Counted quantity" for-id="st-count">
            <input id="st-count" v-model.number="countInput" type="number" min="0" class="st-count-input" />
          </McField>
          <McButton variant="primary" type="button" :disabled="busy" @click="saveLine">Save line</McButton>
        </McCard>
      </div>

      <McCard title="Session lines">
        <table v-if="session.lines.length" class="st-lines mc-table">
          <thead>
            <tr>
              <th>Product</th>
              <th>SKU</th>
              <th>Before</th>
              <th>Counted</th>
              <th>Variance</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in session.lines" :key="l.id">
              <td>{{ l.productName }}</td>
              <td>{{ l.sku }}</td>
              <td>{{ l.qtyBefore }}</td>
              <td>{{ l.qtyCounted }}</td>
              <td :class="{ 'st-var--ok': l.qtyCounted === l.qtyBefore, 'st-var--diff': l.qtyCounted !== l.qtyBefore }">
                {{ l.qtyCounted - l.qtyBefore >= 0 ? '+' : '' }}{{ l.qtyCounted - l.qtyBefore }}
              </td>
            </tr>
          </tbody>
        </table>
        <McEmptyState v-else title="No lines yet" hint="Search and save a counted quantity for each product." />
      </McCard>

      <div v-if="session.status === 'Draft'" class="st-post">
        <McAlert v-if="!canPost" variant="warning">
          Only Admin, Owner, or Dev can post this stocktake and update quantities.
        </McAlert>
        <McButton
          v-else
          variant="danger"
          type="button"
          :disabled="busy || !session.lines.length"
          @click="showPostModal = true"
        >
          Post stocktake
        </McButton>
        <p v-if="canPost && !session.lines.length" class="st-post-hint">Add at least one line before posting.</p>
      </div>

      <McCard v-else title="Complete">
        <p class="mc-text-muted" style="margin: 0">This session is closed. Start a new session for the next count.</p>
      </McCard>
    </template>

    <McModal v-model="showPostModal" title="Post stocktake?">
      <p>This will update on-hand quantities for every line in this session. This cannot be undone from the POS.</p>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showPostModal = false">Cancel</McButton>
        <McButton variant="danger" type="button" :disabled="busy" @click="confirmPost">Post now</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.st-page {
  min-height: 100%;
}

.st-steps {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.25rem;
  margin-bottom: 1.5rem;
  flex-wrap: wrap;
}

.st-step {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.85rem;
  border-radius: 10px;
  background: var(--mc-app-surface-muted, #ebe9e4);
  border: 1px solid var(--mc-app-border-soft, #cfcdc6);
  color: var(--mc-app-text-secondary, #2c2c30);
  font-weight: 600;
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.st-step--active {
  background: rgba(244, 122, 32, 0.15);
  color: #c45f18;
}

.st-step--current {
  outline: 2px solid #f47a20;
  outline-offset: 2px;
}

.st-step__n {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 1.65rem;
  height: 1.65rem;
  border-radius: 50%;
  background: var(--mc-app-surface, #fff);
  font-size: 0.9rem;
  border: 1px solid var(--mc-app-border-faint, #e0ded8);
}

.st-step__line {
  width: 2rem;
  height: 3px;
  background: var(--mc-app-border-subtle, #b5b3ab);
  border-radius: 2px;
}

.st-step__line--on {
  background: #f47a20;
}

.st-session-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 1rem;
  padding: 0.85rem 1rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #cfcdc6);
  border-radius: 12px;
  box-shadow: var(--mc-app-shadow-xs, none);
}

.st-session-badge {
  margin-left: 0.5rem;
}

.st-session-meta {
  margin: 0;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #4a4842);
  font-weight: 500;
}

.st-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1.25rem;
}

@media (min-width: 900px) {
  .st-grid {
    grid-template-columns: 1fr 1fr;
  }
}

.st-scan {
  margin-bottom: 0.75rem;
}

.st-scanner {
  margin-bottom: 1rem;
  padding: 0.75rem;
  background: var(--mc-app-surface-2, #f7f6f3);
  border-radius: 10px;
  border: 1px solid var(--mc-app-border-faint, #e0ded8);
}

.st-search-row {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.st-search-row input {
  flex: 1;
  min-width: 160px;
  min-height: 44px;
}

.st-results {
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
  max-height: 280px;
  overflow-y: auto;
}

.st-result {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 0.5rem;
  align-items: center;
  padding: 0.75rem 0.65rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #e0ded8);
  cursor: pointer;
  border-radius: 8px;
}

.st-result:hover,
.st-result:focus {
  background: rgba(244, 122, 32, 0.06);
  outline: none;
}

.st-result__name {
  font-weight: 600;
  color: var(--mc-app-text, #121214);
}

.st-result__sku {
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #4a4842);
}

.st-result__qty {
  font-size: 0.8rem;
  font-weight: 700;
  color: #2e7d32;
}

.st-counting {
  margin: 0 0 1rem;
}

.st-counting__sub {
  display: block;
  margin-top: 0.35rem;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #4a4842);
  font-weight: 500;
}

.st-count-input {
  font-size: 1.5rem !important;
  font-weight: 700;
  text-align: center;
}

.st-lines {
  width: 100%;
  font-size: 0.9rem;
}

.st-var--ok {
  color: #2e7d32;
  font-weight: 600;
}

.st-var--diff {
  color: #e65100;
  font-weight: 700;
}

.st-post {
  margin-top: 1.25rem;
}

.st-post-hint {
  margin: 0.5rem 0 0;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #4a4842);
  font-weight: 500;
}
</style>
