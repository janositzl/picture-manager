import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiFetch, ApiError } from './client'
import { queryKeys } from './queries'

/** Longest note the server accepts (Folder.NoteMaxLength / Image.NoteMaxLength). */
export const NOTE_MAX_LENGTH = 1024

function putNote(url: string, note: string): Promise<void> {
  return apiFetch<void>(url, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    // An empty note clears it server-side.
    body: JSON.stringify({ note }),
  })
}

export function useSetFolderNote(folderId: number) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (note: string) => putNote(`/api/folders/${folderId}/note`, note),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.folder(folderId) }),
  })
}

export function useSetImageNote(imageId: number) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (note: string) => putNote(`/api/images/${imageId}/note`, note),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.image(imageId) }),
  })
}

/** Maps a failed note save to a message for the user. */
export function noteErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return error.problem?.detail ?? 'A scan is running.'
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't save the note."
}
