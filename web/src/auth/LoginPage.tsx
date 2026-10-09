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

export function LoginPage() {
  const login = useLogin()
  const [error, submit, pending] = useActionState(
    async (_previous: string | null, form: FormData) => {
      try {
        await login.mutateAsync({
          username: String(form.get('username') ?? ''),
          password: String(form.get('password') ?? ''),
        })
        return null
      } catch (caught) {
        return loginErrorMessage(caught)
      }
    },
    null,
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
