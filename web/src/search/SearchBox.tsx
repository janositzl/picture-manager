import SearchIcon from '@mui/icons-material/Search'
import { Box, Chip, InputAdornment, TextField } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useMatch, useNavigate } from 'react-router'
import { useFolder } from '../api/queries'
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
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flex: 1, maxWidth: 560 }}>
      <TextField
        size="small"
        fullWidth
        placeholder="Search file names…"
        value={text}
        onChange={(event) => changeText(event.target.value)}
        slotProps={{
          htmlInput: { 'aria-label': 'Search file names' },
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" />
              </InputAdornment>
            ),
            sx: { bgcolor: 'background.paper' },
          },
        }}
      />
      {scopeId !== null ? (
        <Chip
          label={`in: ${scopeFolder.data?.name ?? '…'}`}
          onDelete={() => changeScope(null)}
          color="secondary"
        />
      ) : (
        currentFolderId !== null && (
          <Chip
            label="In this folder"
            variant="outlined"
            onClick={() => changeScope(currentFolderId)}
            sx={{ color: 'inherit', borderColor: 'currentColor' }}
          />
        )
      )}
    </Box>
  )
}
