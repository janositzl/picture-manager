import { useState } from 'react'

/** A string choice remembered in localStorage; works (unremembered) when storage is unavailable. */
export function useStoredChoice<T extends string>(
  key: string,
  allowed: readonly T[],
  fallback: T,
): [T, (value: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const stored = localStorage.getItem(key)
      return allowed.find((option) => option === stored) ?? fallback
    } catch {
      return fallback
    }
  })
  const set = (next: T) => {
    setValue(next)
    try {
      localStorage.setItem(key, next)
    } catch {
      // Storage unavailable (private mode, blocked): the choice just won't be remembered.
    }
  }
  return [value, set]
}
