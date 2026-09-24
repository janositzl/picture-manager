import { ApiError } from '../api/client'

export type AlbumFormErrors = { name?: string; description?: string; form?: string }

function fieldError(errors: Record<string, string[]>, field: string): string | undefined {
  return Object.entries(errors).find(([key]) => key.toLowerCase() === field)?.[1][0]
}

/** Maps a create/update failure to the field it belongs to. */
export function albumFormErrors(error: unknown): AlbumFormErrors {
  if (error instanceof ApiError) {
    if (error.status === 409) return { name: 'An album with this name already exists.' }
    if (error.status === 400) {
      const errors = error.problem?.errors ?? {}
      const name = fieldError(errors, 'name')
      const description = fieldError(errors, 'description')
      if (name !== undefined || description !== undefined) return { name, description }
    }
  }
  return { form: "Couldn't save the album." }
}
