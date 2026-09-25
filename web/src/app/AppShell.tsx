import { AppBar, Box, Button, Toolbar, Typography } from '@mui/material'
import { NavLink, Outlet } from 'react-router'
import { SearchBox } from '../search/SearchBox'
import { FolderTree } from '../tree/FolderTree'

export function AppShell() {
  return (
    <div className="flex h-screen flex-col">
      <AppBar position="static" elevation={0}>
        <Toolbar variant="dense" sx={{ gap: 2 }}>
          <Typography variant="h6" component="div" sx={{ flexShrink: 0 }}>
            PictureManager
          </Typography>
          <Box sx={{ flex: 1, display: 'flex', justifyContent: 'center' }}>
            <SearchBox />
          </Box>
          <Button color="inherit" component={NavLink} to="/">
            Folders
          </Button>
          <Button color="inherit" component={NavLink} to="/favorites">
            Favorites
          </Button>
          <Button color="inherit" component={NavLink} to="/albums">
            Albums
          </Button>
        </Toolbar>
      </AppBar>
      <div className="flex min-h-0 flex-1">
        <Box
          component="nav"
          aria-label="Folders"
          sx={{
            width: 280,
            flexShrink: 0,
            overflowY: 'auto',
            borderRight: 1,
            borderColor: 'divider',
          }}
        >
          <FolderTree />
        </Box>
        <main className="flex min-w-0 flex-1 flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
