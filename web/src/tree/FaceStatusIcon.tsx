import CheckCircleOutlinedIcon from '@mui/icons-material/CheckCircleOutlined'
import ContrastIcon from '@mui/icons-material/Contrast'
import ErrorOutlinedIcon from '@mui/icons-material/ErrorOutlined'
import RadioButtonUncheckedIcon from '@mui/icons-material/RadioButtonUnchecked'
import { Box, Tooltip } from '@mui/material'
import type { ReactElement } from 'react'
import type { FaceCoverageState } from './faceCoverage'

type Props = { state: Exclude<FaceCoverageState, 'none'>; label: string }

const ICONS: Record<Props['state'], { icon: ReactElement; color: string }> = {
  notScanned: { icon: <RadioButtonUncheckedIcon />, color: 'text.disabled' },
  partial: { icon: <ContrastIcon />, color: 'info.main' },
  needsRescan: { icon: <ErrorOutlinedIcon />, color: 'warning.main' },
  completed: { icon: <CheckCircleOutlinedIcon />, color: 'success.main' },
}

/** The row's face-detection status: ○ not scanned, ◐ partially scanned, ! needs rescan, ✓ completed. */
export function FaceStatusIcon({ state, label }: Props) {
  const { icon, color } = ICONS[state]
  return (
    <Tooltip title={label}>
      <Box
        role="img"
        aria-label={label}
        data-face-state={state}
        sx={{ display: 'inline-flex', ml: 0.5, flexShrink: 0, color, fontSize: 16 }}
      >
        <Box component="span" sx={{ display: 'inline-flex', '& svg': { fontSize: 'inherit' } }}>
          {icon}
        </Box>
      </Box>
    </Tooltip>
  )
}
