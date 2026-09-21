import { ThemeProvider, createTheme, CssBaseline, Button, Typography } from '@mui/material'
import { env } from './config/env'

const theme = createTheme()

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <div className="p-8">
        <Typography variant="h4" gutterBottom>
          PictureManager
        </Typography>
        <Typography variant="body2" className="mb-4">
          API base URL: {env.apiBaseUrl}
        </Typography>
        <Button variant="contained">Scaffold OK</Button>
      </div>
    </ThemeProvider>
  )
}

export default App
