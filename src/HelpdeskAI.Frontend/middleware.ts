import { withAuth } from "next-auth/middleware";

export default withAuth({
  pages: {
    signIn: "/sign-in",
  },
});

export const config = {
  matcher: ["/((?!api/auth|sign-in|signed-out|_next|favicon.ico).*)"],
};
