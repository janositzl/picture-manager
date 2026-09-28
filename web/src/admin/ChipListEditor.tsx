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
}

const HINT = 'Separate multiple entries with commas or spaces.'

/**
 * A labeled list of strings, editable as chips: type or paste one or more (comma/space/newline
 * separated) + Enter/Add to add them, click a chip's × to remove.
 */
export function ChipListEditor({ label, values, onChange, placeholder, disabled, error }: Props) {
  const [draft, setDraft] = useState('')

  const add = () => {
    const existing = new Set(values)
    const additions: string[] = []
    for (const raw of draft.split(/[,\s]+/)) {
      const value = raw.trim()
      if (value === '' || existing.has(value)) continue
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
        helperText={error ?? HINT}
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
