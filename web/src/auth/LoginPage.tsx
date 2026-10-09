import { Alert, Box, Button, Paper, TextField, Typography } from '@mui/material'
import { Aperture } from 'lucide-react'
import { useActionState } from 'react'
import { ApiError } from '../api/client'
import { useLogin } from '../api/auth'
import { ACCENT, HEADING_SX } from '../design/accent'

function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return 'Too many attempts. Try again in a minute.'
    if (error.status === 401) return error.problem?.title ?? 'Invalid username or password.'
  }
  return "Couldn't reach the server."
}

type LoginState = { error: string | null; username: string }

export function LoginPage() {
  const login = useLogin()
  // React resets the form after every action; the username comes back through the state so a typo
  // in the password doesn't make the user retype it.
  const [{ error, username }, submit, pending] = useActionState(
    async (_previous: LoginState, form: FormData): Promise<LoginState> => {
      const username = String(form.get('username') ?? '')
      try {
        await login.mutateAsync({ username, password: String(form.get('password') ?? '') })
        return { error: null, username }
      } catch (caught) {
        return { error: loginErrorMessage(caught), username }
      }
    },
    { error: null, username: '' },
  )

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        bgcolor: 'background.default',
        p: 2,
      }}
    >
      <Paper variant="outlined" sx={{ width: '100%', maxWidth: 360, p: 4, borderRadius: '16px' }}>
        <Box
          component="form"
          action={submit}
          sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
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
            <Typography variant="h6" component="h1" sx={HEADING_SX}>
              PictureManager
            </Typography>
          </Box>
          {error !== null && <Alert severity="error">{error}</Alert>}
          <TextField
            name="username"
            defaultValue={username}
            label="Username"
            autoComplete="username"
            autoFocus
            slotProps={{ htmlInput: { required: true } }}
            size="small"
          />
          <TextField
            name="password"
            label="Password"
            type="password"
            autoComplete="current-password"
            slotProps={{ htmlInput: { required: true } }}
            size="small"
          />
          <Button type="submit" variant="contained" disableElevation disabled={pending}>
            Log in
          </Button>
        </Box>
      </Paper>
    </Box>
  )
}
