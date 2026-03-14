import type { NextConfig } from "next";





const nextConfig: NextConfig = {
  serverExternalPackages: [
    "@copilotkit/runtime",
    "graphql-yoga",
    "@graphql-yoga/plugin-defer-stream",
    "@whatwg-node/fetch",
    "@whatwg-node/server",
    "graphql",
    "reflect-metadata",
    "class-transformer",
    "class-validator",
    "type-graphql",
    "pino",
    "pino-pretty",
  ],
  // Do NOT put AGENT_URL in the env block — that bakes it at build time.
  // Server-side API routes read process.env at runtime, which picks up
  // the Container App env var automatically.
};

export default nextConfig;
