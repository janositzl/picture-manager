# PictureManager web client

React + TypeScript + Vite SPA for PictureManager, using MUI for components and Tailwind CSS v4 for
non-MUI layout utility classes (see the project design spec for why MUI and Tailwind divide styling that
way).

## Development

```bash
npm install
npm run dev      # start the Vite dev server
npm run build    # type-check and production build
npm run lint      # ESLint
npm run format    # Prettier — write
npm run format:check  # Prettier — check only
```

Copy `.env.example` to `.env` and adjust `VITE_API_BASE_URL` if the API isn't running on its default port.
