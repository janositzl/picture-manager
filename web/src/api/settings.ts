import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiFetch, ApiError } from './client'
import { queryKeys } from './queries'
import type { SettingsDto, SettingsSaveResult } from './types'

export type SettingsInput = {
  excludedFolderNames: string[]
  excludedExtensions: string[]
  includedExtensions: string[] | null
}

export function useUpdateSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: SettingsInput) =>
      apiFetch<SettingsSaveResult>('/api/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      }),
    onSuccess: (settings) =>
      queryClient.setQueryData<SettingsDto>(queryKeys.settings(), {
        excludedFolderNames: settings.excludedFolderNames,
        excludedExtensions: settings.excludedExtensions,
        includedExtensions: settings.includedExtensions,
        supportedExtensions: settings.supportedExtensions,
      }),
  })
}

export type SettingsFormErrors = {
  excludedFolderNames?: string
  excludedExtensions?: string
  includedExtensions?: string
  form?: string
}

function fieldError(errors: Record<string, string[]>, field: string): string | undefined {
  return Object.entries(errors).find(([key]) => key.toLowerCase() === field.toLowerCase())?.[1][0]
}

/** Maps a settings-save failure to the section it belongs to. */
export function settingsFormErrors(error: unknown): SettingsFormErrors {
  if (error instanceof ApiError && error.status === 400) {
    const errors = error.problem?.errors ?? {}
    const mapped: SettingsFormErrors = {
      excludedFolderNames: fieldError(errors, 'excludedFolderNames'),
      excludedExtensions: fieldError(errors, 'excludedExtensions'),
      includedExtensions: fieldError(errors, 'includedExtensions'),
    }
    if (mapped.excludedFolderNames || mapped.excludedExtensions || mapped.includedExtensions) {
      return mapped
    }
  }
  return { form: "Couldn't save settings." }
}
