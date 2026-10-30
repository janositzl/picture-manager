import CalendarMonthOutlinedIcon from '@mui/icons-material/CalendarMonthOutlined'
import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import { ToggleButton, ToggleButtonGroup, Tooltip } from '@mui/material'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { BORDER } from '../design/accent'
import { withParams } from '../routing/urlState'
import { writeGroupMode, type GroupMode } from './preferences'

const OPTIONS: Array<{ value: GroupMode; label: string; icon: ReactNode }> = [
  { value: 'all', label: 'All photos', icon: <PhotoLibraryOutlinedIcon fontSize="small" /> },
  { value: 'day', label: 'By day', icon: <CalendarMonthOutlinedIcon fontSize="small" /> },
]

/** All photos / By day. The choice lives in ?group= and is remembered for folders opened without one. */
export function GroupModeToggle({ value }: { value: GroupMode }) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <ToggleButtonGroup
      exclusive
      size="small"
      value={value}
      aria-label="Grouping"
      onChange={(_event, next: GroupMode | null) => {
        if (next === null) return
        writeGroupMode(next)
        setSearchParams(withParams(searchParams, { group: next, image: null }), { replace: true })
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
        // Prefixed: the faces filter beside it has its own "All photos" button.
        <ToggleButton key={option.value} value={option.value} aria-label={`Group: ${option.label}`}>
          <Tooltip title={option.label}>
            <span style={{ display: 'flex' }}>{option.icon}</span>
          </Tooltip>
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  )
}
