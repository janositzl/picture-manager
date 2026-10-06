import FaceIcon from '@mui/icons-material/Face'
import FaceRetouchingOffIcon from '@mui/icons-material/FaceRetouchingOff'
import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import { ToggleButton, ToggleButtonGroup, Tooltip } from '@mui/material'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import type { FacesFilter } from '../api/imageFilter'
import { BORDER } from '../design/accent'
import { withParams } from '../routing/urlState'

type Choice = 'all' | FacesFilter

const OPTIONS: Array<{ value: Choice; label: string; icon: ReactNode }> = [
  { value: 'all', label: 'All photos', icon: <PhotoLibraryOutlinedIcon fontSize="small" /> },
  { value: 'with', label: 'Photos with a face', icon: <FaceIcon fontSize="small" /> },
  { value: 'without', label: 'Photos without a face', icon: <FaceRetouchingOffIcon fontSize="small" /> },
]

/** Three-way icon toggle (all / with face / without face); the choice lives in ?faces= so it survives reloads. */
export function FacesFilterToggle({ value }: { value: FacesFilter | undefined }) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <ToggleButtonGroup
      exclusive
      size="small"
      value={value ?? 'all'}
      aria-label="Faces filter"
      onChange={(_event, next: Choice | null) => {
        if (next !== null)
          setSearchParams(withParams(searchParams, { faces: next === 'all' ? null : next }), { replace: true })
      }}
      sx={{
        border: `1px solid ${BORDER}`,
        borderRadius: '10px',
        bgcolor: 'background.paper',
        overflow: 'hidden',
        '& .MuiToggleButtonGroup-grouped': {
          border: 0,
          borderRadius: 0,
          '&:not(:last-of-type)': { borderRight: `1px solid ${BORDER}` },
        },
      }}
    >
      {OPTIONS.map((option) => (
        <ToggleButton key={option.value} value={option.value} aria-label={option.label}>
          <Tooltip title={option.label}>
            <span style={{ display: 'flex' }}>{option.icon}</span>
          </Tooltip>
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  )
}
