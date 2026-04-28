/**
 * One-shot localStorage key migration.
 *
 * Reads `newKey` first. If absent and `oldKey` has a value, copies it forward
 * and deletes the old entry. Returns the resolved value (or null).
 *
 * Idempotent: once `newKey` exists, subsequent calls are no-ops on storage.
 * Safe in private-browsing / quota-full / SSR contexts — every storage access
 * is try/catch'd.
 */
export function migrateLocalStorageKey(newKey: string, oldKey: string): string | null {
  try {
    const current = localStorage.getItem(newKey)
    if (current !== null) return current

    const legacy = localStorage.getItem(oldKey)
    if (legacy !== null) {
      try { localStorage.setItem(newKey, legacy) } catch { /* quota / private mode */ }
      try { localStorage.removeItem(oldKey) } catch { /* ignore */ }
      return legacy
    }
    return null
  } catch {
    return null
  }
}
