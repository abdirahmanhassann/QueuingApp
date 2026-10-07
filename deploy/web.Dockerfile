# Line (QueuingAppFE) frontend, built to run on Node instead of Vercel. Build context: the QueuingAppFE folder.
FROM node:24-bookworm-slim AS build
WORKDIR /app
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
COPY package.json package-lock.json ./
RUN npm install --no-audit --no-fund
COPY . .
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
# Local .env files (demo/mock settings) must never reach the production build.
RUN rm -f .env .env.local \
 && sed -i 's/preset: "vercel"/preset: "node-server"/' vite.config.ts \
 && node scripts/with-app-env.mjs vite build

FROM node:24-bookworm-slim
WORKDIR /app
COPY --from=build /app/.output ./.output
ENV NODE_ENV=production PORT=3000 HOST=0.0.0.0
EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
