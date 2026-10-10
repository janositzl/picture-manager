import { Avatar } from '@mui/material'
import { alpha } from '@mui/material/styles'
import { ACCENT } from '../design/accent'

/** Up to two initials from the display name. */
export function initials(displayName: string): string {
  const words = displayName.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]!.toUpperCase()).join('') || '?'
}

/** Decorative (the name sits next to it), so it is hidden from assistive tech. */
export function UserAvatar({ displayName, muted = false }: { displayName: string; muted?: boolean }) {
  return (
    <Avatar
      aria-hidden
      sx={{
        width: 36,
        height: 36,
        fontSize: 13,
        fontWeight: 600,
        bgcolor: muted ? 'action.hover' : alpha(ACCENT, 0.14),
        color: muted ? 'text.disabled' : ACCENT,
      }}
    >
      {initials(displayName)}
    </Avatar>
  )
}
