import AddIcon from '@mui/icons-material/Add'
import { Box, Chip, IconButton, InputAdornment, TextField, Typography } from '@mui/material'
import { useState, type KeyboardEvent } from 'react'

type Props = {
  label: string
  values: string[]
  onChange: (values: string[]) => void
  placeholder?: string
  disabled?: boolean
  error?: string
  hint?: string
}

const DEFAULT_HINT = 'Separate multiple entries with commas or spaces. Quote entries ("Old Photos") to keep spaces.'

// Matches a "double-quoted", a 'single-quoted', or a plain comma/whitespace-delimited token.
const TOKEN_PATTERN = /"([^"]*)"|'([^']*)'|[^,\s]+/g

function tokenize(draft: string): string[] {
  const tokens: string[] = []
  for (const match of draft.matchAll(TOKEN_PATTERN)) {
    const value = (match[1] ?? match[2] ?? match[0]).trim()
    if (value !== '') tokens.push(value)
  }
  return tokens
}

/**
 * A labeled list of strings, editable as chips: type or paste one or more (comma/space/newline
 * separated, or "quoted" to keep spaces) + Enter/Add to add them, click a chip's × to remove.
 */
export function ChipListEditor({ label, values, onChange, placeholder, disabled, error, hint }: Props) {
  const [draft, setDraft] = useState('')

  const add = () => {
    const existing = new Set(values)
    const additions: string[] = []
    for (const value of tokenize(draft)) {
      if (existing.has(value)) continue
      existing.add(value)
      additions.push(value)
    }
    if (additions.length === 0) return
    onChange([...values, ...additions])
    setDraft('')
  }

  const remove = (value: string) => onChange(values.filter((v) => v !== value))

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      add()
    }
  }

  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 0.5 }}>
        {label}
      </Typography>
      <TextField
        size="small"
        fullWidth
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
        placeholder={placeholder}
        disabled={disabled}
        error={error !== undefined}
        helperText={error ?? hint ?? DEFAULT_HINT}
        sx={{ mb: 1, '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
        slotProps={{
          input: {
            endAdornment: (
              <InputAdornment position="end">
                <IconButton
                  aria-label={`Add to ${label}`}
                  onClick={add}
                  disabled={disabled || draft.trim() === ''}
                  edge="end"
                  size="small"
                >
                  <AddIcon fontSize="small" />
                </IconButton>
              </InputAdornment>
            ),
          },
        }}
      />
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75 }}>
        {values.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            None
          </Typography>
        ) : (
          values.map((value) => (
            <Chip
              key={value}
              label={value}
              size="small"
              onDelete={disabled ? undefined : () => remove(value)}
            />
          ))
        )}
      </Box>
    </Box>
  )
}
