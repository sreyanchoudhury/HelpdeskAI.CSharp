import { getServerSession } from "next-auth";
import type { NextRequest } from "next/server";
import { authOptions } from "@/lib/auth";

export async function getAuthenticatedUser(_req?: NextRequest) {
  const session = await getServerSession(authOptions);
  const typedSession = session as (typeof session & { accessToken?: string; error?: string }) | null;

  if (!typedSession || typedSession.error) {
    return {
      name: null,
      email: null,
      accessToken: null,
    };
  }

  return {
    name: typeof typedSession.user?.name === "string" ? typedSession.user.name : null,
    email: typeof typedSession.user?.email === "string" ? typedSession.user.email : null,
    accessToken: typeof typedSession.accessToken === "string" ? typedSession.accessToken : null,
  };
}
