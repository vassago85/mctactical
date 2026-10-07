import { computed, ref, watch } from 'vue'
import { http } from '@/api/http'

export type CommissionBasis = 'SalesExVat' | 'GrossProfit'

export type Salesperson = {
  id: string
  name: string
  commissionPercent: number
  commissionBasis: CommissionBasis
  isActive: boolean
}

export function commissionBasisLabel(basis: CommissionBasis): string {
  switch (basis) {
    case 'SalesExVat':
      return 'Sales (excl. VAT)'
    case 'GrossProfit':
      return 'Gross profit (excl. VAT)'
    default: {
      const unreachable: never = basis
      return unreachable
    }
  }
}

/** e.g. "5% of gross profit (excl. VAT)", or "No commission". */
export function commissionSummary(percent: number, basis: CommissionBasis): string {
  if (!percent) return 'No commission'
  const label = commissionBasisLabel(basis)
  return `${percent}% of ${label.charAt(0).toLowerCase()}${label.slice(1)}`
}

const STORAGE_KEY = 'pos.salespersonId'

// Module-level so checkout and the exchange dialog share one list and one remembered pick.
const salespeople = ref<Salesperson[]>([])
const loaded = ref(false)
const selectedId = ref<string | null>(localStorage.getItem(STORAGE_KEY))

watch(selectedId, (id) => {
  if (id) localStorage.setItem(STORAGE_KEY, id)
  else localStorage.removeItem(STORAGE_KEY)
})

/**
 * Active salespeople for the till, plus the operator's last pick (remembered per device so a
 * single person on the till all day only picks once). Selection is required whenever the list
 * is non-empty — the API enforces the same rule.
 */
export function useSalespeople() {
  async function load() {
    try {
      const { data } = await http.get<Salesperson[]>('/api/salespeople')
      salespeople.value = data
      if (selectedId.value && !data.some(s => s.id === selectedId.value)) selectedId.value = null
    } catch {
      salespeople.value = []
    } finally {
      loaded.value = true
    }
  }

  const selected = computed(() => salespeople.value.find(s => s.id === selectedId.value) ?? null)
  const required = computed(() => salespeople.value.length > 0)
  const missing = computed(() => required.value && !selected.value)

  return { salespeople, loaded, selectedId, selected, required, missing, load }
}
