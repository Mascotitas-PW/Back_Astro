# 1. Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["back.csproj", "./"]
RUN dotnet restore "back.csproj"
COPY . .
RUN dotnet publish "back.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Etapa final (AQUÍ SE AGREGA LA LIBRERÍA)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Instalar libgssapi-krb5-2 necesaria para la conexión a PostgreSQL/Supabase
RUN apt-get update && apt-get install -y libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "back.dll"]