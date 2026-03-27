import { withAuth } from "next-auth/middleware";

export default withAuth({
  pages: {
    signIn: "/sign-in",
  },
  callbacks: {
    authorized: ({ token }) => {
      if (!token) return false;
      return token.error !== "RefreshAccessTokenError";
    },
  },
});

export const config = {
  matcher: ["/((?!api/auth|sign-in|signed-out|_next|favicon.ico).*)"],
};
