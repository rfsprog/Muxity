/**
 * PKCE OIDC helpers for Blazor WASM.
 * Uses Web Crypto API (SubtleCrypto) — available in all modern browsers.
 */
window.oidcHelper = {
    generateCodeVerifier: function () {
        const array = new Uint8Array(64);
        crypto.getRandomValues(array);
        return btoa(String.fromCharCode(...array))
            .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    },

    generateCodeChallenge: async function (verifier) {
        const encoder = new TextEncoder();
        const data    = encoder.encode(verifier);
        const digest  = await crypto.subtle.digest('SHA-256', data);
        return btoa(String.fromCharCode(...new Uint8Array(digest)))
            .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    },

    redirect: function (url) {
        window.location.href = url;
    },

    getRedirectUri: function () {
        return `${window.location.origin}/login/callback`;
    },

    saveToSession: function (key, value) {
        sessionStorage.setItem(key, value);
    },

    getFromSession: function (key) {
        return sessionStorage.getItem(key);
    },

    clearSession: function (...keys) {
        keys.forEach(k => sessionStorage.removeItem(k));
    },

    /**
     * Exchanges an authorization code for tokens at the provider's token endpoint.
     * Returns the id_token string, or null on failure.
     */
    exchangeCode: async function (tokenEndpoint, clientId, code, codeVerifier, redirectUri) {
        const body = new URLSearchParams({
            grant_type:    'authorization_code',
            client_id:     clientId,
            code,
            code_verifier: codeVerifier,
            redirect_uri:  redirectUri,
        });

        try {
            const response = await fetch(tokenEndpoint, {
                method:  'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body:    body.toString(),
            });

            if (!response.ok) return null;
            const data = await response.json();
            return data.id_token ?? null;
        } catch {
            return null;
        }
    }
};
