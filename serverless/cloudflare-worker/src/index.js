/**
 * Wadd ToDo - Cloudflare Worker OAuth 2.0 Serverless Proxy
 * 
 * Securely holds the Google OAuth Client Secret and handles
 * token exchange and background refresh for the Desktop App.
 */

export default {
  async fetch(request, env, ctx) {
    // Handle CORS Preflight
    if (request.method === "OPTIONS") {
      return handleCors();
    }

    const url = new URL(request.url);
    const path = url.pathname;

    // Optional App Secret check (if configured in env.APP_PROXY_SECRET)
    if (env.APP_PROXY_SECRET) {
      const authHeader = request.headers.get("x-app-secret") || url.searchParams.get("secret");
      if (authHeader !== env.APP_PROXY_SECRET) {
        return jsonResponse({ error: "unauthorized", error_description: "Invalid application proxy secret" }, 401);
      }
    }

    try {
      // 1. Health check route
      if (path === "/" || path === "/health") {
        return jsonResponse({
          status: "healthy",
          service: "wadd-oauth-proxy",
          timestamp: new Date().toISOString()
        });
      }

      // 2. Exchange Authorization Code for Tokens
      if (path === "/api/auth/token" || path === "/token") {
        if (request.method !== "POST") {
          return jsonResponse({ error: "method_not_allowed", message: "Use POST for token exchange" }, 405);
        }
        return await handleTokenExchange(request, env);
      }

      // 3. Refresh Access Token using Refresh Token
      if (path === "/api/auth/refresh" || path === "/refresh") {
        if (request.method !== "POST") {
          return jsonResponse({ error: "method_not_allowed", message: "Use POST for token refresh" }, 405);
        }
        return await handleTokenRefresh(request, env);
      }

      return jsonResponse({ error: "not_found", message: "Endpoint not found" }, 404);
    } catch (err) {
      return jsonResponse({ error: "server_error", message: err.message || "Internal server error" }, 500);
    }
  }
};

/**
 * Handles authorization code exchange (PKCE + client_secret injection)
 */
async function handleTokenExchange(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return jsonResponse({ error: "invalid_json", message: "Request body must be valid JSON" }, 400);
  }

  const { code, code_verifier, redirect_uri, client_id } = body;

  if (!code) {
    return jsonResponse({ error: "missing_parameter", message: "Authorization code is required" }, 400);
  }

  const clientId = client_id || env.GOOGLE_CLIENT_ID;
  const clientSecret = env.GOOGLE_CLIENT_SECRET;

  if (!clientId) {
    return jsonResponse({ error: "server_misconfigured", message: "GOOGLE_CLIENT_ID is not configured" }, 500);
  }

  if (!clientSecret) {
    return jsonResponse({ error: "server_misconfigured", message: "GOOGLE_CLIENT_SECRET secret is not configured in Worker" }, 500);
  }

  const tokenUrl = "https://oauth2.googleapis.com/token";
  const params = new URLSearchParams({
    grant_type: "authorization_code",
    code: code,
    client_id: clientId,
    client_secret: clientSecret,
    redirect_uri: redirect_uri || "http://localhost:5001/"
  });

  if (code_verifier) {
    params.append("code_verifier", code_verifier);
  }

  const googleResponse = await fetch(tokenUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/x-www-form-urlencoded"
    },
    body: params.toString()
  });

  const responseData = await googleResponse.json();
  return jsonResponse(responseData, googleResponse.status);
}

/**
 * Handles refreshing expired access tokens using the refresh token
 */
async function handleTokenRefresh(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return jsonResponse({ error: "invalid_json", message: "Request body must be valid JSON" }, 400);
  }

  const { refresh_token, client_id } = body;

  if (!refresh_token) {
    return jsonResponse({ error: "missing_parameter", message: "refresh_token is required" }, 400);
  }

  const clientId = client_id || env.GOOGLE_CLIENT_ID;
  const clientSecret = env.GOOGLE_CLIENT_SECRET;

  if (!clientId || !clientSecret) {
    return jsonResponse({ error: "server_misconfigured", message: "GOOGLE_CLIENT_ID or GOOGLE_CLIENT_SECRET missing in Worker" }, 500);
  }

  const tokenUrl = "https://oauth2.googleapis.com/token";
  const params = new URLSearchParams({
    grant_type: "refresh_token",
    refresh_token: refresh_token,
    client_id: clientId,
    client_secret: clientSecret
  });

  const googleResponse = await fetch(tokenUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/x-www-form-urlencoded"
    },
    body: params.toString()
  });

  const responseData = await googleResponse.json();
  return jsonResponse(responseData, googleResponse.status);
}

function handleCors() {
  return new Response(null, {
    status: 204,
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization, x-app-secret",
      "Access-Control-Max-Age": "86400"
    }
  });
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status: status,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization, x-app-secret"
    }
  });
}
