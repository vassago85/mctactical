<script setup lang="ts">
defineProps<{
  modelValue: string
  value: string
  title: string
  description?: string
}>()

defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <label
    class="mc-radio-card"
    :class="{ 'mc-radio-card--selected': modelValue === value }"
    @click="$emit('update:modelValue', value)"
  >
    <input
      type="radio"
      class="mc-radio-card__input"
      :checked="modelValue === value"
      :value="value"
      @change="$emit('update:modelValue', value)"
    />
    <span class="mc-radio-card__body">
      <span class="mc-radio-card__title">{{ title }}</span>
      <span v-if="description" class="mc-radio-card__desc">{{ description }}</span>
    </span>
  </label>
</template>

<style scoped>
.mc-radio-card {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 1rem 1.15rem;
  border-radius: var(--mc-app-radius-control, 10px);
  border: 1.5px solid var(--mc-app-border-soft, #ddd9d3);
  background: var(--mc-app-surface, #fff);
  cursor: pointer;
  transition: border-color 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}
.mc-radio-card:hover {
  border-color: var(--mc-app-border, #9e9c94);
}
.mc-radio-card--selected {
  border-color: var(--mc-accent, #f47a20);
  background: rgba(244, 122, 32, 0.04);
  box-shadow: inset 3px 0 0 var(--mc-accent, #f47a20);
}
.mc-radio-card--selected:hover {
  border-color: var(--mc-accent, #f47a20);
}
.mc-radio-card__input {
  flex-shrink: 0;
  width: 1.2rem;
  height: 1.2rem;
  margin-top: 0.15rem;
  accent-color: var(--mc-accent, #f47a20);
  cursor: pointer;
}
.mc-radio-card__body {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}
.mc-radio-card__title {
  font-weight: 700;
  font-size: 0.9375rem;
  color: var(--mc-app-text, #1a1a1c);
  line-height: 1.3;
}
.mc-radio-card--selected .mc-radio-card__title {
  color: var(--mc-accent-dim, #c45f18);
}
.mc-radio-card__desc {
  font-size: 0.8125rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.45;
}
</style>
