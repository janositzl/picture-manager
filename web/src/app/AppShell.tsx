import ContentCopyOutlinedIcon from '@mui/icons-material/ContentCopyOutlined'
import FolderOutlinedIcon from '@mui/icons-material/FolderOutlined'
import KeyboardDoubleArrowLeftOutlinedIcon from '@mui/icons-material/KeyboardDoubleArrowLeftOutlined'
import KeyboardDoubleArrowRightOutlinedIcon from '@mui/icons-material/KeyboardDoubleArrowRightOutlined'
import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { AppBar, Box, Button, IconButton, Toolbar, Typography } from '@mui/material'
import { Aperture } from 'lucide-react'
import {
  useEffect,
  useRef,
  useState,
  type ComponentType,
  type PointerEvent as ReactPointerEvent,
} from 'react'
import { Link as RouterLink, Outlet, useLocation } from 'react-router'
import { ACCENT, ACCENT_SOFT, ACCENT_TEXT, HEADING_SX } from '../design/accent'
import { SearchBox } from '../search/SearchBox'
import { readStored, writeStored } from '../shared/storage'
import { FolderTree } from '../tree/FolderTree'
import { JobStatusBanner } from '../tree/JobStatusBanner'

const TREE_COLLAPSED_KEY = 'pm.tree.collapsed'
const TREE_WIDTH_KEY = 'pm.tree.width'
const TREE_DEFAULT_WIDTH = 280
const TREE_MIN_WIDTH = 200
const TREE_MAX_WIDTH = 480

function clampTreeWidth(width: number): number {
  return Math.min(TREE_MAX_WIDTH, Math.max(TREE_MIN_WIDTH, width))
}

function readTreeWidth(): number {
  const stored = Number(readStored(TREE_WIDTH_KEY))
  return Number.isFinite(stored) && stored > 0 ? clampTreeWidth(stored) : TREE_DEFAULT_WIDTH
}

const NAV_ITEMS: Array<{ to: string; label: string; icon: ComponentType<{ fontSize?: 'small' }> }> =
  [
    { to: '/', label: 'Folders', icon: FolderOutlinedIcon },
    { to: '/favorites', label: 'Favorites', icon: StarBorderIcon },
    { to: '/albums', label: 'Albums', icon: PhotoLibraryOutlinedIcon },
    { to: '/duplicates', label: 'Duplicates', icon: ContentCopyOutlinedIcon },
  ]

/** "/" immediately redirects to /folders/:id, so the Folders tab must match that whole subtree too. */
function isNavActive(pathname: string, to: string): boolean {
  return to === '/' ? pathname.startsWith('/folders') : pathname.startsWith(to)
}

export function AppShell() {
  const [collapsed, setCollapsed] = useState(() => readStored(TREE_COLLAPSED_KEY) === 'true')
  const [treeWidth, setTreeWidth] = useState(readTreeWidth)
  const location = useLocation()
  const dragRef = useRef<{ startX: number; startWidth: number } | null>(null)

  const toggleCollapsed = () => {
    const next = !collapsed
    setCollapsed(next)
    writeStored(TREE_COLLAPSED_KEY, String(next))
  }

  const onResizePointerDown = (event: ReactPointerEvent) => {
    dragRef.current = { startX: event.clientX, startWidth: treeWidth }
    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'
  }

  useEffect(() => {
    const onPointerMove = (event: PointerEvent) => {
      const drag = dragRef.current
      if (drag === null) return
      setTreeWidth(clampTreeWidth(drag.startWidth + (event.clientX - drag.startX)))
    }
    const onPointerUp = () => {
      if (dragRef.current === null) return
      dragRef.current = null
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
      setTreeWidth((current) => {
        writeStored(TREE_WIDTH_KEY, String(current))
        return current
      })
    }
    window.addEventListener('pointermove', onPointerMove)
    window.addEventListener('pointerup', onPointerUp)
    return () => {
      window.removeEventListener('pointermove', onPointerMove)
      window.removeEventListener('pointerup', onPointerUp)
    }
  }, [])

  return (
    <div className="flex h-screen flex-col">
      <AppBar
        position="static"
        elevation={0}
        sx={{ bgcolor: 'background.paper', color: 'text.primary' }}
      >
        <Toolbar variant="dense" sx={{ gap: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, flexShrink: 0 }}>
            <Box
              sx={{
                display: 'grid',
                placeItems: 'center',
                width: 32,
                height: 32,
                borderRadius: '8px',
                bgcolor: ACCENT,
                color: 'common.white',
              }}
            >
              <Aperture size={20} strokeWidth={2.2} />
            </Box>
            <Typography variant="h6" component="div" sx={HEADING_SX}>
              Picture
              <Box component="span" sx={{ color: ACCENT }}>
                Manager
              </Box>
            </Typography>
          </Box>
          <Box sx={{ flex: 1, display: 'flex', justifyContent: 'center' }}>
            <SearchBox />
          </Box>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            {NAV_ITEMS.map(({ to, label, icon: Icon }) => (
              <Button
                key={to}
                component={RouterLink}
                to={to}
                aria-current={isNavActive(location.pathname, to) ? 'page' : undefined}
                startIcon={<Icon fontSize="small" />}
                sx={{
                  color: 'text.secondary',
                  borderRadius: '999px',
                  textTransform: 'none',
                  fontWeight: 500,
                  px: 1.5,
                  transition: 'background-color 0.2s ease-in-out, color 0.2s ease-in-out',
                  '&:hover': { bgcolor: 'action.hover', color: 'text.primary' },
                  '&[aria-current="page"]': { bgcolor: ACCENT_SOFT, color: ACCENT_TEXT },
                  '&[aria-current="page"]:hover': { bgcolor: ACCENT_SOFT },
                }}
              >
                {label}
              </Button>
            ))}
            <IconButton
              component={RouterLink}
              to="/admin"
              aria-label="Admin"
              size="small"
              sx={{ ml: 0.5, color: 'text.secondary' }}
            >
              <SettingsOutlinedIcon fontSize="small" />
            </IconButton>
          </Box>
        </Toolbar>
      </AppBar>
      <JobStatusBanner />
      <div className="flex min-h-0 flex-1">
        <Box
          component="nav"
          aria-label="Folders"
          style={{ width: collapsed ? 44 : treeWidth }}
          sx={{
            position: 'relative',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
            borderRight: 1,
            borderColor: 'divider',
            transition: dragRef.current === null ? 'width 0.2s ease-in-out' : 'none',
          }}
        >
          {!collapsed && (
            <Box
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize folder panel"
              onPointerDown={onResizePointerDown}
              sx={{
                position: 'absolute',
                top: 0,
                bottom: 0,
                right: -3,
                width: 6,
                cursor: 'col-resize',
                zIndex: 1,
                '&:hover': { bgcolor: 'action.hover' },
              }}
            />
          )}
          {collapsed ? (
            <IconButton
              size="small"
              aria-label="Expand folders"
              onClick={toggleCollapsed}
              sx={{ m: '8px auto' }}
            >
              <KeyboardDoubleArrowRightOutlinedIcon fontSize="small" />
            </IconButton>
          ) : (
            <>
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  px: 2,
                  py: 1.25,
                  flexShrink: 0,
                }}
              >
                <Typography
                  variant="overline"
                  sx={{ fontWeight: 600, letterSpacing: '0.08em', color: 'text.secondary' }}
                >
                  Folders
                </Typography>
                <IconButton size="small" aria-label="Collapse folders" onClick={toggleCollapsed}>
                  <KeyboardDoubleArrowLeftOutlinedIcon fontSize="small" />
                </IconButton>
              </Box>
              <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
                <FolderTree />
              </Box>
            </>
          )}
        </Box>
        <main className="flex min-w-0 flex-1 flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
