import type { NextAuthOptions } from "next-auth";
import AzureADProvider from "next-auth/providers/azure-ad";

const clientId = process.env.AZURE_AD_CLIENT_ID ?? "";
const apiScope = process.env.AZURE_AD_API_SCOPE ?? (clientId ? `api://${clientId}/access_as_user` : "");

export const authOptions: NextAuthOptions = {
  secret: process.env.NEXTAUTH_SECRET,
  pages: {
    signIn: "/sign-in",
  },
  providers: [
    AzureADProvider({
      clientId,
      clientSecret: process.env.AZURE_AD_CLIENT_SECRET ?? "",
      tenantId: process.env.AZURE_AD_TENANT_ID ?? "",
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
      }

      if (profile && typeof profile === "object") {
        token.email = (profile as Record<string, unknown>).preferred_username as string | undefined ?? token.email;
        token.name = (profile as Record<string, unknown>).name as string | undefined ?? token.name;
      }

      return token;
    },
    async session({ session, token }) {
      if (session.user) {
        session.user.email = (token.email as string | undefined) ?? session.user.email;
        session.user.name = (token.name as string | undefined) ?? session.user.name;
      }
      return session;
    },
  },
};
