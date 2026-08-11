<script setup lang="ts">
/**
 * Shopify integration tools (Owner/Dev). Currently hosts category/tag sync; the
 * "Match Shopify sales" tool is added here in a later phase.
 */
import { ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'

const toast = useToast()

type TagPreview = {
  applied: boolean
  linkedProductCount: number
  sample?: { sku: string; tags: string[] }[]
  updatedCount?: number
  failedCount?: number
  failures?: { sku: string; status: number; detail: string }[]
}

const tagBusy = ref(false)
const tagErr = ref<string | null>(null)
const tagPreview = ref<TagPreview | null>(null)

function handleErr(e: unknown, fallback: string): string {
  const ax = e as { response?: { data?: { error?: string } }; message?: string }
  return ax.response?.data?.error ?? ax.message ?? fallback
}

async function previewTags() {
  tagBusy.value = true
  tagErr.value = null
  try {
    const { data } = await http.post<TagPreview>('/api/shopify/sync-tags')
    tagPreview.value = data
  } catch (e) {
    tagErr.value = handleErr(e, 'Preview failed')
    toast.error(tagErr.value)
  } finally {
    tagBusy.value = false
  }
}

async function syncTags() {
  const count = tagPreview.value?.linkedProductCount
  const confirmMsg = count
    ? `Push categories/tags to ${count} linked Shopify product${count === 1 ? '' : 's'}? This updates tags, product type and vendor on Shopify only.`
    : 'Push categories/tags to all linked Shopify products?'
  if (!confirm(confirmMsg)) return

  tagBusy.value = true
  tagErr.value = null
  try {
    const { data } = await http.post<TagPreview>('/api/shopify/sync-tags?apply=true')
    tagPreview.value = data
    const updated = data.updatedCount ?? 0
    const failed = data.failedCount ?? 0
    if (failed > 0) toast.error(`Synced ${updated}, ${failed} failed. See details below.`)
    else toast.success(`Synced tags to ${updated} Shopify product${updated === 1 ? '' : 's'}.`)
  } catch (e) {
    tagErr.value = handleErr(e, 'Sync failed')
    toast.error(tagErr.value)
  } finally {
    tagBusy.value = false
  }
}
</script>

<template>
  <div class="shopify-page">
    <McPageHeader title="Shopify">
      <template #default>
        Tools for keeping the POS and your Shopify store aligned. The POS is the source of truth.
      </template>
    </McPageHeader>

    <McCard title="Categories &amp; tags">
      <p class="shp-hint">
        Push each linked product's <strong>Category</strong>, <strong>Manufacturer</strong> and
        <strong>Item type</strong> to Shopify as tags (and keep product type and vendor in sync), so
        products filter into the same groups in both systems. One-way — no other product fields change.
      </p>

      <McAlert v-if="tagErr" variant="error">{{ tagErr }}</McAlert>

      <div class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="tagBusy" @click="previewTags">
          <McSpinner v-if="tagBusy" />
          <span v-else>Preview</span>
        </McButton>
        <McButton variant="primary" type="button" :disabled="tagBusy || !tagPreview" @click="syncTags">
          Sync now
        </McButton>
      </div>

      <div v-if="tagPreview" class="shp-result">
        <p v-if="!tagPreview.applied" class="shp-hint">
          {{ tagPreview.linkedProductCount }} linked product{{ tagPreview.linkedProductCount === 1 ? '' : 's' }}
          will be updated. Sample of tags to be sent:
        </p>
        <p v-else class="shp-hint">
          Updated {{ tagPreview.updatedCount }} of {{ tagPreview.linkedProductCount }} products.
          <span v-if="tagPreview.failedCount"> {{ tagPreview.failedCount }} failed.</span>
        </p>

        <ul v-if="!tagPreview.applied && tagPreview.sample?.length" class="shp-samples">
          <li v-for="s in tagPreview.sample" :key="s.sku">
            <strong>{{ s.sku }}</strong>
            <span class="shp-tags">
              <span v-for="t in s.tags" :key="t" class="shp-tag">{{ t }}</span>
              <span v-if="!s.tags.length" class="shp-hint">no category fields set</span>
            </span>
          </li>
        </ul>

        <ul v-if="tagPreview.failures?.length" class="shp-failures">
          <li v-for="f in tagPreview.failures" :key="f.sku">
            <strong>{{ f.sku }}</strong> — {{ f.detail }}
          </li>
        </ul>
      </div>
    </McCard>
  </div>
</template>

<style scoped>
.shopify-page {
  min-height: 100%;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: var(--mc-container-width, 1200px);
  margin: 0 auto;
  width: 100%;
}
.shp-hint {
  margin: 0 0 0.75rem;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.55;
}
.shp-actions {
  display: flex;
  gap: 0.6rem;
  margin-top: 0.5rem;
}
.shp-result {
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--mc-app-border-soft, #ddd9d3);
}
.shp-samples {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.shp-samples li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 0.75rem;
  font-size: 0.85rem;
}
.shp-tags {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
.shp-tag {
  background: var(--mc-app-surface-muted, #f0eeea);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 999px;
  padding: 0.1rem 0.55rem;
  font-size: 0.75rem;
  color: var(--mc-app-text, #1a1a1c);
}
.shp-failures {
  margin: 0.75rem 0 0;
  padding-left: 1.1rem;
  font-size: 0.82rem;
  color: var(--mc-app-danger, #9a1818);
}
</style>
