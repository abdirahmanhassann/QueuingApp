# Line (QueueingApp) API. Build context: the QueueingApp repo root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY QueueingApp/QueueingApp.csproj QueueingApp/
RUN dotnet restore QueueingApp/QueueingApp.csproj
COPY QueueingApp/ QueueingApp/
RUN dotnet publish QueueingApp/QueueingApp.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueueingApp.dll"]
