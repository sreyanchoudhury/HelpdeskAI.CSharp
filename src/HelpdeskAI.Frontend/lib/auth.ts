import type { NextAuthOptions } from "next-auth";
import AzureADProvider from "next-auth/providers/azure-ad";

const clientId = process.env.AZURE_AD_CLIENT_ID ?? "";
const clientSecret = process.env.AZURE_AD_CLIENT_SECRET ?? "";
const tenantId = process.env.AZURE_AD_TENANT_ID ?? "";
const apiScope = process.env.AZURE_AD_API_SCOPE ?? (clientId ? `api://${clientId}/access_as_user` : "");
const refreshBufferMs = 60_000;

type TokenShape = {
  accessToken?: string;
  accessTokenExpires?: number;
  refreshToken?: string;
  error?: string;
  email?: string;
  name?: string;
};

async function refreshAccessToken(token: TokenShape): Promise<TokenShape> {
  if (!token.refreshToken || !clientId || !clientSecret || !tenantId) {
    return { ...token, error: "RefreshAccessTokenError" };
  }

  try {
    const response = await fetch(`https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`, {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        grant_type: "refresh_token",
        refresh_token: token.refreshToken,
        scope: ["openid", "profile", "email", "offline_access", apiScope].filter(Boolean).join(" "),
      }),
    });

    const refreshed = await response.json();
    if (!response.ok) {
      throw new Error(refreshed.error_description ?? refreshed.error ?? "Token refresh failed");
    }

    return {
      ...token,
      accessToken: typeof refreshed.access_token === "string" ? refreshed.access_token : token.accessToken,
      accessTokenExpires: Date.now() + (Number(refreshed.expires_in ?? 3600) * 1000),
      refreshToken: typeof refreshed.refresh_token === "string" ? refreshed.refresh_token : token.refreshToken,
      error: undefined,
    };
  } catch {
    return { ...token, error: "RefreshAccessTokenError" };
  }
}

export const authOptions: NextAuthOptions = {
  secret: process.env.NEXTAUTH_SECRET,
  pages: {
    signIn: "/sign-in",
  },
  providers: [
    AzureADProvider({
      clientId,
      clientSecret,
      tenantId,
      authorization: {
        params: {
          scope: ["openid", "profile", "email", "offline_access", apiScope]
            .filter(Boolean)
            .join(" "),
        },
      },
    }),
  ],
  callbacks: {
    async jwt({ token, account, profile }) {
      if (account?.access_token) {
        token.accessToken = account.access_token;
        token.accessTokenExpires = Date.now() + (Number(account.expires_in ?? 3600) * 1000);
      }

      if (account?.refresh_token) {
        token.refreshToken = account.refresh_token;
      }

      if (profile && typeof profile === "object") {
        token.email = (profile as Record<string, unknown>).preferred_username as string | undefined ?? token.email;
        token.name = (profile as Record<string, unknown>).name as string | undefined ?? token.name;
      }

      if (typeof token.accessTokenExpires === "number" && Date.now() < token.accessTokenExpires - refreshBufferMs) {
        return token;
      }

      return refreshAccessToken(token as TokenShape);
    },
    async session({ session, token }) {
      if (session.user) {
        session.user.email = (token.email as string | undefined) ?? session.user.email;
        session.user.name = (token.name as string | undefined) ?? session.user.name;
      }

      if (typeof token.accessToken === "string") {
        (session as typeof session & { accessToken?: string }).accessToken = token.accessToken;
      }

      if (typeof token.error === "string") {
        (session as typeof session & { error?: string }).error = token.error;
      }

      return session;
    },
  },
};
