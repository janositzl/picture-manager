import { useSyncExternalStore } from 'react'
import { readStored, writeStored } from '../shared/storage'

export type TileSize = 'small' | 'medium' | 'large'

export const TILE_MIN_WIDTH: Record<TileSize, number> = { small: 100, medium: 150, large: 180 }

const STORAGE_KEY = 'pm.grid.tileSize'
const DEFAULT_SIZE: TileSize = 'large'

function isTileSize(value: string | null): value is TileSize {
  return value === 'small' || value === 'medium' || value === 'large'
}

const listeners = new Set<() => void>()

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

// Reads straight from storage rather than caching in a module variable, so every subscriber sees
// the same value and tests can reset state between runs by clearing storage.
function getSnapshot(): TileSize {
  const stored = readStored(STORAGE_KEY)
  return isTileSize(stored) ? stored : DEFAULT_SIZE
}

function setTileSize(size: TileSize): void {
  writeStored(STORAGE_KEY, size)
  for (const listener of listeners) listener()
}

/** Shared across every grid on screen, so the size picked in one header applies everywhere. */
export function useTileSize(): [TileSize, (size: TileSize) => void] {
  const size = useSyncExternalStore(subscribe, getSnapshot)
  return [size, setTileSize]
}
