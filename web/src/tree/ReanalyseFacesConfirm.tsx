import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  ToggleButton,
  ToggleButtonGroup,
} from '@mui/material'
import type { FacePreset } from '../api/jobs'
import { useStoredChoice } from '../people/useStoredChoice'

type Props = {
  folderName: string
  recursive: boolean
  onConfirm: (preset: FacePreset) => void
  onClose: () => void
}

const PRESETS: readonly FacePreset[] = ['fast', 'detailed']

const HINT: Record<FacePreset, string> = {
  fast: 'Quicker. Finds clearly visible faces.',
  detailed: 'Slower, roughly twice as long. Also finds small and distant faces.',
}

/** Re-analysing runs the (slow) detector again on every photo, so it is confirmed first, with how closely to look. */
export function ReanalyseFacesConfirm({ folderName, recursive, onConfirm, onClose }: Props) {
  const [preset, setPreset] = useStoredChoice<FacePreset>(
    'pm.faces.reanalysePreset',
    PRESETS,
    'detailed',
  )

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Re-analyse faces?</DialogTitle>
      <DialogContent>
        <DialogContentText>
          {`Analyse every photo in ${folderName}${recursive ? ' and its subfolders' : ''} for faces again? This can take a while. People you have already confirmed are kept.`}
        </DialogContentText>
        <ToggleButtonGroup
          exclusive
          fullWidth
          size="small"
          value={preset}
          aria-label="Detection"
          onChange={(_event, next: FacePreset | null) => {
            if (next !== null) setPreset(next)
          }}
          sx={{ mt: 2 }}
        >
          <ToggleButton value="fast" sx={{ textTransform: 'none' }}>
            Fast
          </ToggleButton>
          <ToggleButton value="detailed" sx={{ textTransform: 'none' }}>
            Detailed
          </ToggleButton>
        </ToggleButtonGroup>
        <DialogContentText sx={{ mt: 1, fontSize: 13 }}>{HINT[preset]}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => onConfirm(preset)}>
          Re-analyse
        </Button>
      </DialogActions>
    </Dialog>
  )
}
