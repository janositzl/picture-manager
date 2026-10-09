import AccountCircleOutlinedIcon from '@mui/icons-material/AccountCircleOutlined'
import { Divider, IconButton, ListItemText, Menu, MenuItem, Tooltip } from '@mui/material'
import { useState } from 'react'
import { useCurrentUser, useLogout } from '../api/auth'
import { ChangePasswordDialog } from './ChangePasswordDialog'

export function UserMenu() {
  const me = useCurrentUser().data
  const logout = useLogout()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [changing, setChanging] = useState(false)
  if (!me) return null

  return (
    <>
      <Tooltip title={me.displayName}>
        <IconButton
          aria-label="Account"
          size="small"
          onClick={(event) => setAnchor(event.currentTarget)}
          sx={{ color: 'text.secondary' }}
        >
          <AccountCircleOutlinedIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        <MenuItem disabled>
          <ListItemText primary={me.displayName} secondary={me.username} />
        </MenuItem>
        <Divider />
        <MenuItem
          onClick={() => {
            setAnchor(null)
            setChanging(true)
          }}
        >
          Change password
        </MenuItem>
        <MenuItem
          onClick={() => {
            setAnchor(null)
            logout.mutate()
          }}
        >
          Log out
        </MenuItem>
      </Menu>
      {changing && <ChangePasswordDialog onClose={() => setChanging(false)} />}
    </>
  )
}
