# Wadd ToDo - Cloudflare Worker OAuth 2.0 Proxy

Serverless OAuth 2.0 PKCE proxy for **Wadd**. Isolates `GOOGLE_CLIENT_SECRET` on Cloudflare's edge network so client binaries never embed OAuth secrets.

---

## Architecture

```
serverless/cloudflare-worker/
├── src/
│   └── index.js            # OAuth 2.0 Token Exchange & Refresh API
└── wrangler.toml           # Worker deployment configuration
```

---

## API Endpoints

| Path | Method | Description |
|---|---|---|
| `/health` | GET | Health check status endpoint |
| `/api/auth/token` | POST | Exchanges PKCE authorization `code` + `code_verifier` for Google OAuth tokens |
| `/api/auth/refresh` | POST | Refreshes expired access tokens using a stored `refresh_token` |

---

## Deploying via Wrangler CLI

```bash
cd serverless/cloudflare-worker

# 1. Login to Cloudflare
npx wrangler login

# 2. Set Google Client Secret (one-time)
npx wrangler secret put GOOGLE_CLIENT_SECRET

# 3. Deploy Worker
npx wrangler deploy
```

---

## Environment Variables & Secrets

Configure via the Cloudflare Dashboard or `wrangler.toml` / `wrangler secret`:

- `GOOGLE_CLIENT_SECRET` *(Secret)*: Google OAuth Web Client Secret from Google Cloud Console.
- `GOOGLE_CLIENT_ID` *(Vars / Optional)*: Default Google Client ID.
- `APP_PROXY_SECRET` *(Secret / Optional)*: Shared secret header (`x-app-secret`) to restrict API calls to your app instance.
