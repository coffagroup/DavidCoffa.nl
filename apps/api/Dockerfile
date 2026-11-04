FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/Coffa.Api/Coffa.Api.csproj apps/api/Coffa.Api/
RUN dotnet restore apps/api/Coffa.Api/Coffa.Api.csproj
COPY apps/api/Coffa.Api/ apps/api/Coffa.Api/
RUN dotnet publish apps/api/Coffa.Api/Coffa.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
USER app
CMD ["dotnet", "Coffa.Api.dll"]
