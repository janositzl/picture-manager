import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiFetch, ApiError } from './client'
import { queryKeys } from './queries'

export function useRestoreFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (folderId: number) =>
      apiFetch<void>(`/api/folders/${folderId}/restore`, { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.removedFolders() })
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
    },
  })
}

export function restoreFolderErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't restore the folder."
}

export function useDeleteRemovedFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (folderId: number) =>
      apiFetch<void>(`/api/folders/${folderId}/removed`, { method: 'DELETE' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.removedFolders() })
    },
  })
}

export function deleteRemovedFolderErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't delete the folder."
}
