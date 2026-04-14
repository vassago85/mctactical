<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import BarcodeScanner from '@/components/BarcodeScanner.vue'

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  sellPrice: number
  qtyOnHand: number
  cost?: number | null
}

type Line = { product: Product; qty: number; unitPrice: number; lineDiscount: number }

const q = ref('')
const results = ref<Product[]>([])
const cart = ref<Line[]>([])
const scanOpen = ref(false)
const customerName = ref('')
const customerEmail = ref('')
const customerType = ref('')
const paymentMethod = ref('Cash')
const discountTotal = ref(0)
const sendEmail = ref(false)
const busy = ref(false)
const err = ref<string | null>(null)
const auth = useAuthStore()
const isManager = ref(false)
const posRules = ref<{
  maxCartDiscountPercent: number
  maxLineDiscountPercent: number
  maxPriceDecreasePercentFromList: number
  maxPriceIncreasePercentFromList: number
  isManager: boolean
} | null>(null)

onMounted(async () => {
  try {
    const { data } = await http.get('/api/settings/pos-rules')
    posRules.value = data
    isManager.value = data.isManager ?? false
  } catch {
    posRules.value = null
  }
})

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void runSearch(), 250)
})

async function runSearch() {
  err.value = null
  const s = q.value.trim()
  if (!s) {
    results.value = []
    return
  }
  try {
    const { data } = await http.get<Product[]>('/api/products', {
      params: { q: s, take: 40 }
    })
    results.value = data
  } catch {
    results.value = []
  }
}

function addToCart(p: Product) {
  const existing = cart.value.find((l) => l.product.id === p.id)
  if (existing) {
    if (existing.qty >= p.qtyOnHand) return
    existing.qty += 1
  } else {
    if (p.qtyOnHand < 1) return
    cart.value.push({ product: p, qty: 1, unitPrice: p.sellPrice, lineDiscount: 0 })
  }
}

function onScan(code: string) {
  q.value = code.trim()
  scanOpen.value = false
  void (async () => {
    await runSearch()
    const exact =
      results.value.find((p) => p.barcode === code.trim() || p.sku === code.trim()) ?? results.value[0]
    if (exact) addToCart(exact)
  })()
}

const subTotal = computed(() =>
  cart.value.reduce((s, l) => s + Math.max(0, l.unitPrice * l.qty - l.lineDiscount), 0)
)

const grandPreview = computed(() => Math.max(0, subTotal.value - discountTotal.value))

const totalCost = computed(() =>
  cart.value.reduce((s, l) => s + (l.product.cost ?? 0) * l.qty, 0)
)

const belowCostWarning = computed(() => {
  if (!isManager.value || cart.value.length === 0) return null
  if (grandPreview.value < totalCost.value && totalCost.value > 0)
    return `Sale total R${grandPreview.value.toFixed(2)} is below total cost R${totalCost.value.toFixed(2)}`
  return null
})

async function checkout() {
  err.value = null
  if (!cart.value.length) return
  if (belowCostWarning.value && !confirm(`${belowCostWarning.value}. Proceed anyway?`)) return
  busy.value = true
  try {
    const { data } = await http.post('/api/invoices', {
      customerName: customerName.value || null,
      customerEmail: customerEmail.value || null,
      customerType: customerType.value || null,
      paymentMethod: paymentMethod.value,
      discountTotal: discountTotal.value,
      sendEmail: sendEmail.value && !!customerEmail.value.trim(),
      lines: cart.value.map((l) => ({
        productId: l.product.id,
        quantity: l.qty,
        unitPriceOverride: l.unitPrice !== l.product.sellPrice ? l.unitPrice : null,
        lineDiscount: l.lineDiscount
      }))
    })
    cart.value = []
    discountTotal.value = 0
    let msg = `Invoice ${data.invoiceNumber} — total R${data.grandTotal?.toFixed?.(2) ?? data.grandTotal}`
    if (data.belowCostWarning) msg += `\n\n⚠ ${data.belowCostWarning}`
    alert(msg)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Checkout failed'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <h1>Point of sale</h1>
  <div v-if="posRules" class="card" style="font-size: 0.82rem; color: var(--mc-muted)">
    <strong style="color: var(--mc-text)">POS rules</strong>
    <span v-if="isManager"> — Manager mode: discounts and price edits enabled. Below-cost sales will show a warning.</span>
    <span v-else> — Sales staff: sell at listed price only. No discounts or price changes. Ask a manager to override.</span>
  </div>
  <p class="err" v-if="err">{{ err }}</p>

  <div class="row" style="margin-bottom: 1rem">
    <button type="button" class="btn secondary" @click="scanOpen = !scanOpen">
      {{ scanOpen ? 'Hide scanner' : 'Scan barcode' }}
    </button>
  </div>
  <div v-if="scanOpen" class="card">
    <BarcodeScanner :active="scanOpen" @decode="onScan" />
  </div>

  <div class="field">
    <label>Search products</label>
    <input v-model="q" placeholder="SKU, barcode, name…" />
  </div>
  <div class="card">
    <table>
      <thead>
        <tr>
          <th>Name</th>
          <th>SKU</th>
          <th>Price</th>
          <th>Stock</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in results" :key="p.id">
          <td>{{ p.name }}</td>
          <td>{{ p.sku }}</td>
          <td>{{ p.sellPrice.toFixed(2) }}</td>
          <td>{{ p.qtyOnHand }}</td>
          <td><button type="button" class="btn secondary" @click="addToCart(p)">Add</button></td>
        </tr>
      </tbody>
    </table>
  </div>

  <h2>Cart</h2>
  <div class="card">
    <table>
      <thead>
        <tr>
          <th>Item</th>
          <th>Qty</th>
          <th v-if="isManager">Price</th>
          <th v-else>Price</th>
          <th v-if="isManager">Line disc</th>
          <th>Line total</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="l in cart" :key="l.product.id">
          <td>{{ l.product.name }}</td>
          <td>
            <input type="number" v-model.number="l.qty" min="1" :max="l.product.qtyOnHand" style="width: 4rem" />
          </td>
          <td v-if="isManager">
            <input type="number" v-model.number="l.unitPrice" step="0.01" style="width: 5rem" />
          </td>
          <td v-else>{{ l.unitPrice.toFixed(2) }}</td>
          <td v-if="isManager">
            <input type="number" v-model.number="l.lineDiscount" step="0.01" min="0" style="width: 5rem" />
          </td>
          <td>{{ Math.max(0, l.unitPrice * l.qty - l.lineDiscount).toFixed(2) }}</td>
        </tr>
      </tbody>
    </table>
    <p>
      Subtotal: <strong>R{{ subTotal.toFixed(2) }}</strong>
      <template v-if="isManager && discountTotal > 0"> — after order discount: <strong>R{{ grandPreview.toFixed(2) }}</strong></template>
    </p>
    <p v-if="belowCostWarning" style="color: #ef5350; font-weight: 600">
      ⚠ {{ belowCostWarning }}
    </p>
  </div>

  <div class="card">
    <div class="field">
      <label>Customer name</label>
      <input v-model="customerName" />
    </div>
    <div class="field">
      <label>Customer email (for receipt link)</label>
      <input v-model="customerEmail" type="email" />
    </div>
    <div class="field">
      <label>Customer type (optional)</label>
      <input v-model="customerType" placeholder="e.g. ENT" />
    </div>
    <div class="field">
      <label>Payment</label>
      <select v-model="paymentMethod">
        <option>Cash</option>
        <option>Card</option>
        <option>Bank</option>
      </select>
    </div>
    <div v-if="isManager" class="field">
      <label>Order discount (R)</label>
      <input type="number" v-model.number="discountTotal" step="0.01" min="0" />
    </div>
    <label><input type="checkbox" v-model="sendEmail" /> Email invoice link</label>
    <div style="margin-top: 1rem">
      <button type="button" class="btn" :disabled="busy || !cart.length" @click="checkout">Complete sale</button>
    </div>
  </div>
</template>
