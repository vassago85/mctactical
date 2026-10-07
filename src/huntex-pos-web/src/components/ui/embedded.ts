import type { InjectionKey } from 'vue'

/** True inside a tab of a parent page, so page headers render as section headers. */
export const EMBEDDED_PAGE: InjectionKey<boolean> = Symbol('embedded-page')
