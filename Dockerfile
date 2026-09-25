FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyExpenses.csproj ./
RUN dotnet restore MyExpenses.csproj

COPY . ./
RUN dotnet publish MyExpenses.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /app/Data && chown -R app:app /app
COPY --from=build --chown=app:app /app/publish ./

USER app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    Storage__DataDirectory=/app/Data \
    ReverseProxy__UseForwardedHeaders=true
EXPOSE 10000

ENTRYPOINT ["dotnet", "MyExpenses.dll"]
