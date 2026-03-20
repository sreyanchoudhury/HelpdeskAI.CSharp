import { NextRequest } from "next/server";
import { getToken } from "next-auth/jwt";

export async function getAuthenticatedUser(req: NextRequest) {
  const token = await getToken({ req, secret: process.env.NEXTAUTH_SECRET });
  return {
    name: typeof token?.name === "string" ? token.name : null,
    email: typeof token?.email === "string" ? token.email : null,
    accessToken: typeof token?.accessToken === "string" ? token.accessToken : null,
  };
}
