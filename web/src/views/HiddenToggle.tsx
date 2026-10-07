import { FormControlLabel, Switch } from '@mui/material'
import { useSearchParams } from 'react-router'
import { withParams } from '../routing/urlState'

/** The folder view's "Show hidden" switch; the choice lives in ?hidden=1 so it survives reloads. */
export function HiddenToggle({ checked }: { checked: boolean }) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <FormControlLabel
      label="Show hidden"
      control={
        <Switch
          size="small"
          checked={checked}
          onChange={(_event, next) =>
            setSearchParams(withParams(searchParams, { hidden: next ? 1 : null }), {
              replace: true,
            })
          }
        />
      }
      sx={{ mr: 0, '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 600 } }}
    />
  )
}
