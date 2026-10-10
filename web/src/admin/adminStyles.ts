import { alpha } from '@mui/material/styles'
import { ACCENT } from '../design/accent'

export const FIELD_SX = { '& .MuiOutlinedInput-root': { borderRadius: '10px' } } as const
export const DIALOG_PAPER_SLOT = { paper: { className: 'rounded-2xl' } } as const
export const BUTTON_SX = {
  borderRadius: '10px',
  textTransform: 'none',
  transition: 'all 200ms ease-in-out',
} as const
export const PRIMARY_BUTTON_SX = { ...BUTTON_SX, fontWeight: 600 } as const

/** The rounded, hairline-bordered surface every admin list sits on. */
export const CARD_SX = {
  bgcolor: 'background.paper',
  border: 1,
  borderColor: 'divider',
  borderRadius: '16px',
  boxShadow: '0 1px 2px rgba(16, 24, 40, 0.04)',
  overflow: 'hidden',
} as const

export const ACCENT_CHIP_SX = {
  bgcolor: alpha(ACCENT, 0.14),
  color: ACCENT,
  fontWeight: 600,
  border: 'none',
} as const
