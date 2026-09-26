import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiFetch, ApiError } from './client'

type SetFolderExcludedArgs = { folderId: number; isExcluded: boolean }

export function setFolderExcluded(folderId: number, isExcluded: boolean): Promise<void> {
  return apiFetch<void>(`/api/folders/${folderId}/exclusion`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ isExcluded }),
  })
}

export function useSetFolderExcluded() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ folderId, isExcluded }: SetFolderExcludedArgs) =>
      setFolderExcluded(folderId, isExcluded),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['folders'] }),
  })
}

/** Maps a failed exclusion toggle to a message for the notification snackbar. */
export function folderExclusionErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return error.problem?.detail ?? 'A scan is running.'
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't change exclusion."
}
