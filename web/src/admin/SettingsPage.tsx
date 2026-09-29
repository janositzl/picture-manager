import {
  Alert,
  Box,
  Button,
  FormControlLabel,
  Switch,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import { settingsFormErrors, useUpdateSettings, type SettingsFormErrors } from '../api/settings'
import { useSettings } from '../api/queries'
import { useNotify } from '../app/notify'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import type { SettingsDto } from '../api/types'
import { ChipListEditor } from './ChipListEditor'

export function SettingsPage() {
  const settings = useSettings()
  const update = useUpdateSettings()
  const notify = useNotify()

  const [excludedFolderNames, setExcludedFolderNames] = useState<string[]>([])
  const [excludedExtensions, setExcludedExtensions] = useState<string[]>([])
  const [allExtensionsAllowed, setAllExtensionsAllowed] = useState(true)
  const [includedExtensions, setIncludedExtensions] = useState<string[]>([])
  const [errors, setErrors] = useState<SettingsFormErrors>({})
  // Adjusting state during render (not in an effect) when the loaded settings change, per
  // https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes
  const [loadedSettings, setLoadedSettings] = useState<SettingsDto | undefined>(undefined)
  if (settings.data !== undefined && settings.data !== loadedSettings) {
    setLoadedSettings(settings.data)
    setExcludedFolderNames(settings.data.excludedFolderNames)
    setExcludedExtensions(settings.data.excludedExtensions)
    setAllExtensionsAllowed(settings.data.includedExtensions === null)
    setIncludedExtensions(settings.data.includedExtensions ?? [])
  }

  let body: ReactNode
  if (settings.isPending) {
    body = null
  } else if (settings.isError) {
    body = <QueryErrorAlert message="Couldn't load settings." onRetry={() => void settings.refetch()} />
  } else {
    const save = async () => {
      setErrors({})
      try {
        const result = await update.mutateAsync({
          excludedFolderNames,
          excludedExtensions,
          includedExtensions: allExtensionsAllowed ? null : includedExtensions,
        })
        if (result.pruneOnNextScan) {
          notify('Saved. The next scan will remove newly-excluded files and folders.')
        } else {
          notify('Settings saved.')
        }
      } catch (error) {
        setErrors(settingsFormErrors(error))
      }
    }

    body = (
      <Box sx={{ p: 3, maxWidth: 640 }}>
        {errors.form !== undefined && (
          <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>
            {errors.form}
          </Alert>
        )}
        <ChipListEditor
          label="Excluded folder names"
          values={excludedFolderNames}
          onChange={setExcludedFolderNames}
          placeholder="e.g. Deleted"
          error={errors.excludedFolderNames}
        />
        <ChipListEditor
          label="Excluded extensions"
          values={excludedExtensions}
          onChange={setExcludedExtensions}
          placeholder="e.g. .RW2"
          error={errors.excludedExtensions}
          hint="Separate multiple entries with commas or spaces."
        />
        <Box sx={{ mb: 1 }}>
          <FormControlLabel
            control={
              <Switch
                checked={allExtensionsAllowed}
                onChange={(event) => setAllExtensionsAllowed(event.target.checked)}
              />
            }
            label="All extensions allowed"
          />
          <Typography variant="body2" color="text.secondary">
            {allExtensionsAllowed ? 'All of: ' : 'Only these can ever be indexed: '}
            {settings.data.supportedExtensions.join(', ')}
          </Typography>
        </Box>
        {!allExtensionsAllowed && (
          <ChipListEditor
            label="Included extensions"
            values={includedExtensions}
            onChange={setIncludedExtensions}
            placeholder="e.g. .jpg"
            error={errors.includedExtensions}
            hint="Separate multiple entries with commas or spaces."
          />
        )}
        <Button
          variant="contained"
          disableElevation
          disabled={update.isPending}
          onClick={() => void save()}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600, mt: 1 }}
        >
          Save
        </Button>
      </Box>
    )
  }

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ px: 3, pt: 2 }}>
        Folders and file extensions to exclude from scanning. Changes apply on the next scan.
      </Typography>
      {body}
    </>
  )
}
