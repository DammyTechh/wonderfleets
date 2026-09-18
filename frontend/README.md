# WonderFleet — web

React + TypeScript front end for the WonderFleet platform (Ofemini Global Limited).

```bash
npm install
npm run dev     # http://localhost:5173, proxying /api to http://localhost:8080
npm run build   # type-check and emit dist/
```

## What is in here

| Route | Audience | Notes |
|---|---|---|
| `/sign-in` | Administrators | The only way in; there is no sign-up. |
| `/`, `/fleet`, `/fleet/new`, `/tracking`, `/partners`, `/processors`, `/analytics`, `/alerts`, `/notifications`, `/settings` | Administrators | Full console, including the four-tab **Add a Fleet** wizard. |
| `/track?t=…` | Share-link recipients | Exchanges the link for a short portal session, then redirects. |
| `/portal/logistics` | Transporters | Fleet and positions, **no cargo climate data**. |
| `/portal/agro` | Produce owners | Position plus temperature and humidity history. |

## Conventions

- **Design system** in `tailwind.config.js` and `src/index.css`: brand greens, semantic alert colours,
  Plus Jakarta Sans for headings, Inter for interface text, JetBrains Mono for codes and readings.
- **Icons** are [lucide-react](https://lucide.dev) throughout — no emoji stand-ins.
- **Data** flows through TanStack Query hooks in `src/lib/queries.ts`; the axios client in `src/lib/api.ts`
  refreshes the access token once on a 401 and replays the request.
- **Realtime** (`src/lib/realtime.ts`) is additive: pages also poll, so a dropped socket never freezes the UI.
- Tokens are held in memory (admin) or `sessionStorage` (portal); the refresh token stays in an HttpOnly cookie.

## Environment

```
VITE_API_BASE_URL=https://wonderfleet-api.onrender.com   # empty in dev, the proxy handles it
```
