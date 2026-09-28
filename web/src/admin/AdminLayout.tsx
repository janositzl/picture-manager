import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { Box, IconButton, Tab, Tabs, Typography } from '@mui/material'
import { Link as RouterLink, Outlet, useLocation } from 'react-router'
import { HEADING_SX } from '../design/accent'

const TABS = [
  { to: '/admin/settings', label: 'Settings' },
  { to: '/admin/roots', label: 'Roots' },
  { to: '/admin/removed-folders', label: 'Removed folders' },
]

export function AdminLayout() {
  const location = useLocation()
  const current = TABS.find((tab) => location.pathname.startsWith(tab.to))?.to ?? TABS[0]!.to

  return (
    <div className="flex h-screen flex-col">
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 2.5, py: 1.25 }}>
        <IconButton component={RouterLink} to="/" aria-label="Back to app" size="small">
          <ArrowBackIcon fontSize="small" />
        </IconButton>
        <Typography variant="h6" component="h1" sx={HEADING_SX}>
          Admin
        </Typography>
      </Box>
      <Tabs value={current} sx={{ borderBottom: 1, borderColor: 'divider', px: 2 }}>
        {TABS.map((tab) => (
          <Tab key={tab.to} value={tab.to} label={tab.label} component={RouterLink} to={tab.to} />
        ))}
      </Tabs>
      <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
        <Outlet />
      </Box>
    </div>
  )
}
