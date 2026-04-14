<script setup lang="ts">
defineProps<{
  modelValue: boolean
  label: string
  hint?: string
}>()

defineEmits<{
  'update:modelValue': [value: boolean]
}>()
</script>

<template>
  <label class="mc-check">
    <input
      type="checkbox"
      class="mc-check__input"
      :checked="modelValue"
      @change="$emit('update:modelValue', ($event.target as HTMLInputElement).checked)"
    />
    <span class="mc-check__body">
      <span class="mc-check__label">{{ label }}</span>
      <span v-if="hint" class="mc-check__hint">{{ hint }}</span>
    </span>
  </label>
</template>

<style scoped>
.mc-check {
  display: flex;
  align-items: flex-start;
  gap: 0.65rem;
  cursor: pointer;
  padding: 0.5rem 0;
}
.mc-check__input {
  flex-shrink: 0;
  width: 1.25rem;
  height: 1.25rem;
  margin-top: 0.1rem;
  accent-color: var(--mc-accent, #f47a20);
  cursor: pointer;
}
.mc-check__body {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}
.mc-check__label {
  font-weight: 600;
  font-size: 0.9375rem;
  color: var(--mc-app-text-secondary, #333336);
  line-height: 1.4;
}
.mc-check__hint {
  font-size: 0.8125rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.4;
}
</style>
