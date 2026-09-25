import { createTheme, alpha } from '@mui/material/styles';

// "Light table" direction: quiet, neutral chrome so photographs carry the colour.
// Accent is cyanotype blue, a nod to early photographic printing.
// Dark scheme uses a true neutral grey (not tinted black) so it doesn't cast
// a hue onto images, the same reason photo editors use neutral grey surrounds.

const cyanotype = '#1D4E89';
const cyanotypeLight = '#8FB4E3';

const fontStack = '"Public Sans", system-ui, -apple-system, "Segoe UI", sans-serif';

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: 'data' },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: cyanotype, contrastText: '#FFFFFF' },
        secondary: { main: '#5B6570' },
        background: { default: '#EEF0F2', paper: '#FFFFFF' },
        text: { primary: '#1C2126', secondary: '#5B6570' },
        divider: '#D6DADF',
        warning: { main: '#B26A00' }, // e.g. "missing file" state
      },
    },
    dark: {
      palette: {
        primary: { main: cyanotypeLight, contrastText: '#0F1B2B' },
        secondary: { main: '#A3ABB4' },
        background: { default: '#1E1F21', paper: '#26282B' },
        text: { primary: '#E6E8EA', secondary: '#A3ABB4' },
        divider: '#3A3D41',
        warning: { main: '#E0A040' },
      },
    },
  },

  shape: { borderRadius: 8 },

  typography: {
    fontFamily: fontStack,
    fontSize: 14,
    h1: { fontSize: '2rem', fontWeight: 600, letterSpacing: '-0.02em' },
    h2: { fontSize: '1.5rem', fontWeight: 600, letterSpacing: '-0.015em' },
    h3: { fontSize: '1.25rem', fontWeight: 600 },
    h4: { fontSize: '1.125rem', fontWeight: 600 },
    h5: { fontSize: '1rem', fontWeight: 600 },
    h6: { fontSize: '0.9375rem', fontWeight: 600 },
    body1: { lineHeight: 1.55 },
    body2: { lineHeight: 1.5 },
    button: { textTransform: 'none', fontWeight: 600, letterSpacing: 0 },
    overline: { textTransform: 'none', letterSpacing: 0, fontWeight: 600 },
  },

  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: { WebkitFontSmoothing: 'antialiased' },
        // Tabular figures keep EXIF columns (ISO, f-stop, dates) aligned
        '.tabular, td': { fontVariantNumeric: 'tabular-nums' },
        '@media (prefers-reduced-motion: reduce)': {
          '*': { transitionDuration: '0.01ms !important', animationDuration: '0.01ms !important' },
        },
      },
    },

    // Flat surfaces separated by hairlines instead of stacked shadows
    MuiPaper: {
      defaultProps: { elevation: 0 },
      styleOverrides: { root: { backgroundImage: 'none' } },
    },
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'inherit' },
      styleOverrides: {
        root: ({ theme }) => ({
          backgroundColor: theme.vars.palette.background.paper,
          borderBottom: `1px solid ${theme.vars.palette.divider}`,
        }),
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: ({ theme }) => ({ borderRight: `1px solid ${theme.vars.palette.divider}` }),
      },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: { root: { borderRadius: 10 } },
    },

    // Image tiles: tight radius so thumbnails read as photos, not UI cards
    MuiImageListItem: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 3,
          overflow: 'hidden',
          outline: '1px solid transparent',
          '&:hover': { outlineColor: theme.vars.palette.divider },
          '&:focus-within': { outline: `2px solid ${theme.vars.palette.primary.main}`, outlineOffset: 2 },
          '& img': { display: 'block' },
        }),
      },
    },

    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: { root: { borderRadius: 6, paddingInline: 14 } },
    },
    MuiIconButton: {
      styleOverrides: { root: { borderRadius: 6 } },
    },
    MuiButtonBase: {
      styleOverrides: {
        root: ({ theme }) => ({
          '&.Mui-focusVisible': { outline: `2px solid ${theme.vars.palette.primary.main}`, outlineOffset: 2 },
        }),
      },
    },

    MuiTextField: { defaultProps: { size: 'small' } },
    MuiOutlinedInput: {
      styleOverrides: { root: { borderRadius: 6 } },
    },

    MuiChip: {
      defaultProps: { size: 'small' },
      styleOverrides: { root: { borderRadius: 4, fontWeight: 500 } },
    },

    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: 6,
          marginInline: 8,
          '&.Mui-selected': {
            backgroundColor: `rgba(${theme.vars.palette.primary.mainChannel} / 0.12)`,
            color: theme.vars.palette.primary.main,
            '& .MuiListItemIcon-root': { color: 'inherit' },
          },
        }),
      },
    },

    MuiTooltip: {
      defaultProps: { arrow: true, enterDelay: 400 },
      styleOverrides: { tooltip: { fontSize: '0.75rem', fontWeight: 500 } },
    },

    MuiDialog: {
      styleOverrides: { paper: { borderRadius: 12 } },
    },
    MuiBackdrop: {
      styleOverrides: {
        root: { backgroundColor: alpha('#000', 0.72) }, // darker, for full-size image viewing
      },
    },

    MuiTableCell: {
      styleOverrides: {
        head: ({ theme }) => ({ fontWeight: 600, color: theme.vars.palette.text.secondary }),
      },
    },
  },
});
