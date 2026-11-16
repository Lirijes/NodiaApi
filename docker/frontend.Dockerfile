# ---------------------------------------------------------------
# Nodia frontend – UTVECKLINGS-image (inte för produktion)
#
# Build context är frontendrepot (../nodia), se compose.yaml.
# Imagen innehåller Node 22 + node_modules installerade FÖR LINUX.
# Källkoden monteras in via compose.yaml så att Vite ser ändringar.
# ---------------------------------------------------------------
FROM node:22-bookworm-slim

WORKDIR /app

# 1) Bara package-filerna först -> npm ci cachas tills de ändras.
COPY package.json package-lock.json ./
RUN npm ci

# 2) Vites utvecklingsserver.
EXPOSE 5173

# 3) --host 0.0.0.0 krävs: utan det lyssnar Vite bara på containerns
#    egen localhost och går inte att nå från din webbläsare.
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "5173", "--strictPort"]
