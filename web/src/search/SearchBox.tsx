import SearchIcon from '@mui/icons-material/Search'
import { Box, Chip, TextField } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useMatch, useNavigate } from 'react-router'
import { useFolder } from '../api/queries'
import { ACCENT, BORDER, SURFACE_MUTED } from '../design/accent'
import { parseId, parseSearchState } from '../routing/urlState'

export const SEARCH_DEBOUNCE_MS = 300

/** File-name search in the app bar. Typing navigates to /search after a pause. */
export function SearchBox() {
  const navigate = useNavigate()
  const location = useLocation()
  const folderMatch = useMatch('/folders/:folderId')
  const onSearchRoute = location.pathname === '/search'
  const fromUrl = parseSearchState(new URLSearchParams(location.search))

  const [text, setText] = useState(onSearchRoute ? fromUrl.q : '')
  const [scopeId, setScopeId] = useState<number | null>(onSearchRoute ? fromUrl.in : null)
  const currentFolderId = parseId(folderMatch?.params.folderId ?? null)
  const scopeFolder = useFolder(scopeId)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const submit = (nextText: string, nextScope: number | null) => {
    const q = nextText.trim()
    // Clearing the box elsewhere shouldn't jump to an empty search page.
    if (q === '' && !onSearchRoute) return
    const params = new URLSearchParams()
    if (q !== '') params.set('q', q)
    if (nextScope !== null) params.set('in', String(nextScope))
    const search = params.size > 0 ? `?${params}` : ''
    // Refining a search replaces the entry, so Back leaves search instead of replaying keystrokes.
    navigate({ pathname: '/search', search }, { replace: onSearchRoute })
  }

  const changeText = (next: string) => {
    setText(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => submit(next, scopeId), SEARCH_DEBOUNCE_MS)
  }

  const changeScope = (next: number | null) => {
    setScopeId(next)
    // On /search an empty query still lists photos, so the scope must reach the URL too.
    if (text.trim() !== '' || onSearchRoute) submit(text, next)
  }

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 0.75,
        flex: 1,
        maxWidth: 560,
        pl: 1.5,
        pr: 0.5,
        py: 0.25,
        borderRadius: '999px',
        border: `1px solid ${BORDER}`,
        bgcolor: 'action.hover',
        transition: 'background-color 0.2s ease-in-out, box-shadow 0.2s ease-in-out',
        '&:focus-within': {
          bgcolor: 'background.paper',
          boxShadow: `0 0 0 4px ${ACCENT}1a`,
          borderColor: ACCENT,
        },
      }}
    >
      <SearchIcon fontSize="small" sx={{ color: 'text.secondary', flexShrink: 0, lineHeight: 0 }} />
      <TextField
        variant="standard"
        size="small"
        fullWidth
        placeholder="Search file names…"
        value={text}
        onChange={(event) => changeText(event.target.value)}
        slotProps={{
          htmlInput: { 'aria-label': 'Search file names' },
          input: { disableUnderline: true },
        }}
        sx={{
          '& .MuiInputBase-root': { alignItems: 'center' },
          '& .MuiInputBase-input': { py: 0, lineHeight: '20px' },
        }}
      />
      {scopeId !== null ? (
        <Chip
          size="small"
          label={`in: ${scopeFolder.data?.name ?? '…'}`}
          onDelete={() => changeScope(null)}
          sx={{
            flexShrink: 0,
            borderRadius: '999px',
            bgcolor: ACCENT,
            color: 'common.white',
            '& .MuiChip-deleteIcon': { color: 'rgba(255,255,255,0.8)' },
            '& .MuiChip-deleteIcon:hover': { color: 'common.white' },
          }}
        />
      ) : (
        currentFolderId !== null && (
          <Chip
            size="small"
            label="In this folder"
            variant="outlined"
            onClick={() => changeScope(currentFolderId)}
            sx={{
              flexShrink: 0,
              color: 'text.secondary',
              bgcolor: 'transparent',
              borderColor: 'transparent',
              borderRadius: '999px',
              '&:hover': { bgcolor: SURFACE_MUTED, color: 'text.primary' },
            }}
          />
        )
      )}
    </Box>
  )
}
