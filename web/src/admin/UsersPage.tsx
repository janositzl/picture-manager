import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined'
import EditOutlinedIcon from '@mui/icons-material/EditOutlined'
import LockResetOutlinedIcon from '@mui/icons-material/LockResetOutlined'
import {
  Box,
  Button,
  Chip,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { alpha } from '@mui/material/styles'
import { useState } from 'react'
import { useCurrentUser } from '../api/auth'
import { useUsers } from '../api/users'
import type { UserSummary } from '../api/types'
import { ACCENT, HEADING_SX } from '../design/accent'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { ACCENT_CHIP_SX, CARD_SX, PRIMARY_BUTTON_SX } from './adminStyles'
import { DeleteUserDialog } from './DeleteUserDialog'
import { ResetPasswordDialog } from './ResetPasswordDialog'
import { UserAvatar } from '../shared/UserAvatar'
import { UserCreateDialog } from './UserCreateDialog'
import { UserEditDialog } from './UserEditDialog'

const HEAD_CELL_SX = {
  color: 'text.secondary',
  fontSize: 12,
  fontWeight: 600,
  letterSpacing: '0.04em',
  textTransform: 'uppercase',
  whiteSpace: 'nowrap',
  py: 1.25,
  bgcolor: 'action.hover',
} as const

function lastLogin(value: string | null): string {
  return value === null ? 'Never' : new Date(value).toLocaleString()
}

const ACTION_SX = {
  color: 'text.secondary',
  transition: 'all 200ms ease-in-out',
  '&:hover': { color: ACCENT, bgcolor: alpha(ACCENT, 0.1) },
} as const

const DELETE_ACTION_SX = {
  color: 'text.secondary',
  transition: 'all 200ms ease-in-out',
  '&:hover': { color: 'error.main', bgcolor: (theme: { palette: { error: { main: string } } }) => alpha(theme.palette.error.main, 0.1) },
} as const

function UserRow({ user, isSelf }: { user: UserSummary; isSelf: boolean }) {
  const dimmed = !user.isActive
  const [dialog, setDialog] = useState<'edit' | 'reset' | 'delete' | null>(null)
  const close = () => setDialog(null)
  return (
    <TableRow
      hover
      sx={{
        transition: 'background-color 200ms ease-in-out',
        '&:last-child td': { borderBottom: 0 },
        '& td': { py: 1.5 },
      }}
    >
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 220 }}>
          <UserAvatar displayName={user.displayName} muted={dimmed} />
          <Box sx={{ minWidth: 0 }}>
            <Typography
              variant="body2"
              sx={{ fontWeight: 600 }}
              color={dimmed ? 'text.secondary' : 'text.primary'}
              noWrap
            >
              {user.displayName}
            </Typography>
            <Typography variant="caption" color="text.secondary" noWrap component="div">
              {user.username}
            </Typography>
          </Box>
        </Box>
      </TableCell>
      <TableCell>
        <Chip
          size="small"
          label={user.role}
          sx={user.role === 'Admin' ? ACCENT_CHIP_SX : { bgcolor: 'action.selected', fontWeight: 500 }}
        />
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {user.role === 'Admin' ? 'Always' : user.canRunFolderActions ? 'Yes' : 'No'}
        </Typography>
      </TableCell>
      <TableCell>
        <Chip
          size="small"
          variant="outlined"
          color={user.isActive ? 'success' : 'default'}
          label={user.isActive ? 'Active' : 'Disabled'}
        />
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary" noWrap>
          {lastLogin(user.lastLoginAt)}
        </Typography>
      </TableCell>
      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>
        {user.albumCount}
      </TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
        <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 0.5 }}>
          <Tooltip title={`Edit ${user.username}`}>
            <IconButton size="small" aria-label={`Edit ${user.username}`} onClick={() => setDialog('edit')} sx={ACTION_SX}>
              <EditOutlinedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title={`Reset password for ${user.username}`}>
            <IconButton size="small" aria-label={`Reset password for ${user.username}`} onClick={() => setDialog('reset')} sx={ACTION_SX}>
              <LockResetOutlinedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title={isSelf ? "You can't delete your own account" : `Delete ${user.username}`}>
            <span>
              <IconButton size="small" aria-label={`Delete ${user.username}`} disabled={isSelf} onClick={() => setDialog('delete')} sx={DELETE_ACTION_SX}>
                <DeleteOutlinedIcon fontSize="small" />
              </IconButton>
            </span>
          </Tooltip>
        </Box>
        {dialog === 'edit' && <UserEditDialog user={user} isSelf={isSelf} onClose={close} />}
        {dialog === 'reset' && <ResetPasswordDialog user={user} onClose={close} />}
        {dialog === 'delete' && <DeleteUserDialog user={user} onClose={close} />}
      </TableCell>
    </TableRow>
  )
}

export function UsersPage() {
  const users = useUsers()
  const selfId = useCurrentUser().data?.id
  const [creating, setCreating] = useState(false)

  if (users.isPending) return null
  if (users.isError) {
    return <QueryErrorAlert message="Couldn't load users." onRetry={() => void users.refetch()} />
  }

  const count = users.data.length
  return (
    <Box sx={{ p: { xs: 2, sm: 3 }, maxWidth: 1100, mx: 'auto' }}>
      <Box sx={CARD_SX}>
        <Box
          sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2, px: 3, py: 2.5 }}
        >
          <Box>
            <Typography variant="h6" component="h2" sx={HEADING_SX}>
              Users
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {count} {count === 1 ? 'user' : 'users'}
            </Typography>
          </Box>
          <Button
            variant="contained"
            size="small"
            disableElevation
            onClick={() => setCreating(true)}
            sx={{ ...PRIMARY_BUTTON_SX, bgcolor: ACCENT, px: 2, '&:hover': { bgcolor: '#4b4bc4' } }}
          >
            New user
          </Button>
        </Box>
        <TableContainer sx={{ borderTop: 1, borderColor: 'divider' }}>
          <Table size="small" aria-label="Users">
            <TableHead>
              <TableRow>
                <TableCell sx={HEAD_CELL_SX}>User</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Role</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Folder actions</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Status</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Last login</TableCell>
                <TableCell align="right" sx={HEAD_CELL_SX}>Albums</TableCell>
                <TableCell align="right" sx={HEAD_CELL_SX}>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {users.data.map((user) => (
                <UserRow key={user.id} user={user} isSelf={user.id === selfId} />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Box>
      {creating && <UserCreateDialog onClose={() => setCreating(false)} />}
    </Box>
  )
}
