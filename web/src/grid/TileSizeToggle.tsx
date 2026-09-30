import GridViewIcon from '@mui/icons-material/GridView'
import ViewComfyIcon from '@mui/icons-material/ViewComfy'
import ViewModuleIcon from '@mui/icons-material/ViewModule'
import { ToggleButton, ToggleButtonGroup, Tooltip } from '@mui/material'
import type { ReactNode } from 'react'
import { BORDER } from '../design/accent'
import { useTileSize, type TileSize } from './tileSize'

const OPTIONS: Array<{ value: TileSize; label: string; icon: ReactNode }> = [
  { value: 'small', label: 'Small thumbnails', icon: <GridViewIcon fontSize="small" /> },
  { value: 'medium', label: 'Medium thumbnails', icon: <ViewModuleIcon fontSize="small" /> },
  { value: 'large', label: 'Large thumbnails', icon: <ViewComfyIcon fontSize="small" /> },
]

export function TileSizeToggle() {
  const [size, setSize] = useTileSize()

  return (
    <ToggleButtonGroup
      exclusive
      size="small"
      value={size}
      aria-label="Thumbnail size"
      onChange={(_event, value: TileSize | null) => {
        if (value !== null) setSize(value)
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
