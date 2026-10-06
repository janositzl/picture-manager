import { ToggleButton, ToggleButtonGroup } from '@mui/material'
import type { FaceMode } from './faceModes'

type Props = { value: FaceMode; onChange: (mode: FaceMode) => void; label: string }

const SEGMENTED_SX = {
  p: 0.375,
  gap: 0.25,
  borderRadius: 999,
  bgcolor: 'action.hover',
  '& .MuiToggleButton-root': {
    border: 0,
    borderRadius: '999px !important',
    px: 1.5,
    py: 0.25,
    textTransform: 'none',
    fontWeight: 500,
    color: 'text.secondary',
    transition: 'all 200ms ease-in-out',
  },
  '& .MuiToggleButton-root.Mui-selected, & .MuiToggleButton-root.Mui-selected:hover': {
    bgcolor: 'background.paper',
    color: 'text.primary',
    boxShadow: '0 1px 2px rgba(0,0,0,0.08)',
  },
} as const

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
      sx={SEGMENTED_SX}
    >
      <ToggleButton value="photos">Photos</ToggleButton>
      <ToggleButton value="faces">Faces</ToggleButton>
    </ToggleButtonGroup>
  )
}
