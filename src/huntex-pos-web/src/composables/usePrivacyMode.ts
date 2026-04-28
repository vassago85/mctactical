import { ref, watch } from 'vue'
import { migrateLocalStorageKey } from '@/utils/storageMigrate'

const STORAGE_KEY = 'pos:privacy-mode'
const LEGACY_STORAGE_KEY = 'mctactical:privacy-mode'
const CLASS_NAME = 'privacy-mode'

const initial = (() => {
  try {
    return migrateLocalStorageKey(STORAGE_KEY, LEGACY_STORAGE_KEY) === '1'
  } catch {
    return false
  }
})()

const privacyActive = ref(initial)

if (typeof document !== 'undefined') {
  if (initial) {
    document.body.classList.add(CLASS_NAME)
  }
  watch(privacyActive, (on) => {
    document.body.classList.toggle(CLASS_NAME, on)
    try {
      localStorage.setItem(STORAGE_KEY, on ? '1' : '0')
    } catch { /* ignore quota / private mode */ }
  })
}

export function usePrivacyMode() {
  function toggle() {
    privacyActive.value = !privacyActive.value
  }
  function set(on: boolean) {
    privacyActive.value = on
  }
  return { privacyActive, toggle, set }
}
