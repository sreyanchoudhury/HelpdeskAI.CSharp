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
  env: {
    AGENT_URL: process.env.AGENT_URL ?? "http://localhost:5200/agent",
  },
};

export default nextConfig;
