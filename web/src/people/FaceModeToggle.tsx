import { ToggleButton, ToggleButtonGroup } from '@mui/material'
import type { FaceMode } from './faceModes'

type Props = { value: FaceMode; onChange: (mode: FaceMode) => void; label: string }

/** Full photos or the small face crop of the person, per tile. */
export function FaceModeToggle({ value, onChange, label }: Props) {
  return (
    <ToggleButtonGroup
      size="small"
      exclusive
      value={value}
      aria-label={label}
      onChange={(_, next: FaceMode | null) => {
        if (next !== null) onChange(next)
      }}
    >
      <ToggleButton value="photos">Photos</ToggleButton>
      <ToggleButton value="faces">Faces</ToggleButton>
    </ToggleButtonGroup>
  )
}
