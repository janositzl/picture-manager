import { createTheme } from '@mui/material/styles';

const border = '#e3e5ec';

export const theme = createTheme({
  shape: { borderRadius: 10 },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#5b5bd6', contrastText: '#ffffff' },
        background: { default: '#f3f4f7', paper: '#ffffff' },
        text: { primary: '#1b1e28', secondary: '#6b7180' },
        divider: border,
        action: { hover: '#ecebfb', selected: '#ecebfb' },
      },
    },
    dark: true,
  },
  components: {
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiOutlinedInput: {
      styleOverrides: { notchedOutline: { borderColor: border } },
    },
    MuiCard: {
      styleOverrides: { root: { borderColor: border } },
    },
  },
});
