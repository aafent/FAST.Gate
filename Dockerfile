# Base image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy the csproj using the subfolder structure and restore dependencies
COPY ["FAST.Gate/FAST.Gate.csproj", "FAST.Gate/"]
RUN dotnet restore "FAST.Gate/FAST.Gate.csproj"

# Copy the remaining source code and build
COPY . .
WORKDIR "/src/FAST.Gate"
RUN dotnet build "FAST.Gate.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish the application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FAST.Gate.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage: copy published files and define entrypoint
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FAST.Gate.dll"]
