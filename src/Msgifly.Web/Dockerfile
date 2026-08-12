# syntax=docker/dockerfile:1

# ---- Stage 1: build frontend assets (Tailwind + Alpine bundle via Vite) ----
FROM node:20-alpine AS frontend-build
WORKDIR /app/src/Msgifly.Web
COPY src/Msgifly.Web/frontend/package*.json ./frontend/
RUN cd frontend && npm ci
COPY src/Msgifly.Web/frontend/ ./frontend/
COPY src/Msgifly.Web/Views/ ./Views/
COPY src/Msgifly.Web/Areas/ ./Areas/
RUN cd frontend && npm run build

# ---- Stage 2: restore, build & publish the .NET app ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/Msgifly.Web/Msgifly.Web.csproj src/Msgifly.Web/
RUN dotnet restore src/Msgifly.Web/Msgifly.Web.csproj
COPY src/Msgifly.Web/ src/Msgifly.Web/
COPY --from=frontend-build /app/src/Msgifly.Web/wwwroot/build/ src/Msgifly.Web/wwwroot/build/
RUN dotnet publish src/Msgifly.Web/Msgifly.Web.csproj -c Release -o /app/publish --no-restore

# ---- Stage 3: runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Msgifly.Web.dll"]
