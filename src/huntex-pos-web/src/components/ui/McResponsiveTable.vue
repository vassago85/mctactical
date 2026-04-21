<script setup lang="ts">
import { computed } from 'vue'

interface Column {
  key: string
  label: string
  align?: 'left' | 'right' | 'center'
  /** Hide on phones (<= 640px) to keep table legible. */
  hiddenOnMobile?: boolean
  /** Width hint, e.g. "120px" or "20%". */
  width?: string
  /** Column shown as the card title on mobile. */
  primary?: boolean
}

const props = defineProps<{
  columns: Column[]
  rows: Array<Record<string, unknown>>
  rowKey: string
  emptyText?: string
  /** When true, stack rows as cards on narrow screens. */
  mobileStack?: boolean
}>()

const primaryKey = computed(() =>
  props.columns.find(c => c.primary)?.key ?? props.columns[0]?.key ?? props.rowKey)
</script>

<template>
  <div class="mc-rt" :class="{ 'mc-rt--stack': mobileStack !== false }">
    <table class="mc-rt__table" role="table">
      <thead>
        <tr>
          <th
            v-for="c in columns"
            :key="c.key"
            :class="[
              c.align === 'right' ? 'text-right' : '',
              c.align === 'center' ? 'text-center' : '',
              c.hiddenOnMobile ? 'mc-rt__hide-sm' : ''
            ]"
            :style="c.width ? { width: c.width } : undefined"
          >{{ c.label }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="String(row[rowKey])">
          <td
            v-for="c in columns"
            :key="c.key"
            :class="[
              c.align === 'right' ? 'text-right' : '',
              c.align === 'center' ? 'text-center' : '',
              c.hiddenOnMobile ? 'mc-rt__hide-sm' : ''
            ]"
            :data-label="c.label"
            :data-primary="c.key === primaryKey ? 'true' : undefined"
          >
            <slot :name="`cell-${c.key}`" v-bind="{ row, column: c, value: row[c.key] }">
              {{ row[c.key] ?? '—' }}
            </slot>
          </td>
        </tr>
        <tr v-if="rows.length === 0">
          <td :colspan="columns.length" class="mc-rt__empty">{{ emptyText ?? 'No data' }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.mc-rt {
  width: 100%;
  overflow-x: auto;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 12px;
  background: var(--mc-app-surface, #fff);
}
.mc-rt__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.mc-rt__table thead th {
  background: var(--mc-app-table-head-bg, #f4f3f0);
  text-align: left;
  font-size: 0.72rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--mc-app-text-muted, #5c5a56);
  padding: 0.65rem 0.85rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}
.mc-rt__table tbody td {
  padding: 0.75rem 0.85rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  vertical-align: middle;
}
.mc-rt__table tbody tr:last-child td {
  border-bottom: 0;
}
.mc-rt__table tbody tr:nth-child(even) td {
  background: var(--mc-app-table-row-alt, #faf9f7);
}
.text-right { text-align: right; }
.text-center { text-align: center; }

.mc-rt__empty {
  text-align: center;
  padding: 1.25rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

@media (max-width: 640px) {
  .mc-rt--stack {
    border: 0;
    background: transparent;
  }
  .mc-rt--stack .mc-rt__table,
  .mc-rt--stack .mc-rt__table tbody,
  .mc-rt--stack .mc-rt__table tr,
  .mc-rt--stack .mc-rt__table td {
    display: block;
    width: 100%;
  }
  .mc-rt--stack .mc-rt__table thead { display: none; }
  .mc-rt--stack .mc-rt__hide-sm { display: none; }
  .mc-rt--stack .mc-rt__table tr {
    background: var(--mc-app-surface, #fff);
    border: 1px solid var(--mc-app-border-faint, #eceae5);
    border-radius: 12px;
    padding: 0.75rem;
    margin-bottom: 0.6rem;
  }
  .mc-rt--stack .mc-rt__table td {
    border: 0;
    padding: 0.35rem 0;
    display: flex;
    justify-content: space-between;
    gap: 1rem;
    text-align: right;
  }
  .mc-rt--stack .mc-rt__table td::before {
    content: attr(data-label);
    font-size: 0.7rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--mc-app-text-muted, #5c5a56);
    font-weight: 600;
    text-align: left;
    flex: 0 0 auto;
  }
  .mc-rt--stack .mc-rt__table td[data-primary='true'] {
    flex-direction: column;
    align-items: flex-start;
    text-align: left;
    font-weight: 700;
    border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
    padding-bottom: 0.5rem;
    margin-bottom: 0.25rem;
  }
  .mc-rt--stack .mc-rt__table td[data-primary='true']::before {
    display: none;
  }
  .mc-rt--stack .mc-rt__table tbody tr:nth-child(even) td {
    background: transparent;
  }
}
</style>
