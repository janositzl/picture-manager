import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiFetch, ApiError } from './client'
import { queryKeys } from './queries'
import type { RootSummary } from './types'

export type RootUpdateInput = { name?: string; alias?: string | null; isActive?: boolean }
export type RootCreateInput = { name: string; mountPath: string; alias: string | null }

export function useCreateRoot() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: RootCreateInput) =>
      apiFetch<RootSummary>('/api/roots', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roots() })
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
    },
  })
}

export type RootCreateFormErrors = { name?: string; mountPath?: string; alias?: string; form?: string }

function fieldError(errors: Record<string, string[]>, field: string): string | undefined {
  return Object.entries(errors).find(([key]) => key.toLowerCase() === field.toLowerCase())?.[1][0]
}

/** Maps a root-create failure to the field it belongs to. */
export function rootCreateFormErrors(error: unknown): RootCreateFormErrors {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return { form: error.problem?.detail ?? 'A root with that name, alias or mount path already exists.' }
    }
    if (error.status === 400) {
      const errors = error.problem?.errors ?? {}
      const mapped: RootCreateFormErrors = {
        name: fieldError(errors, 'name'),
        mountPath: fieldError(errors, 'mountPath'),
        alias: fieldError(errors, 'alias'),
      }
      if (mapped.name || mapped.mountPath || mapped.alias) return mapped
    }
  }
  return { form: "Couldn't create the root." }
}

export function useUpdateRoot(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: RootUpdateInput) =>
      apiFetch<RootSummary>(`/api/roots/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      }),
    onSuccess: (root) =>
      queryClient.setQueryData<RootSummary[]>(queryKeys.roots(), (roots) =>
        roots?.map((r) => (r.id === root.id ? root : r)),
      ),
  })
}

/** Maps a root-save failure to a message for the caller (row-level, no per-field form here). */
export function rootUpdateErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return 'A root with that name or export segment already exists.'
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't save the root."
}

export function useDeleteRoot() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => apiFetch<void>(`/api/roots/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roots() })
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
    },
  })
}
