import { Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function EmptyMessage({ children }: { children: ReactNode }) {
  return (
    <Typography sx={{ p: 3 }} color="text.secondary">
      {children}
    </Typography>
  )
}
