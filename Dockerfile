# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file using the specific folder structure
COPY ["FAST.Gate/FAST.Gate.csproj", "FAST.Gate/"]

# Restore NuGet packages
RUN dotnet restore "FAST.Gate/FAST.Gate.csproj"

# Copy the remaining source code
COPY . .

# Build and publish the application
WORKDIR "/src/FAST.Gate"
RUN dotnet publish "FAST.Gate.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Copy the published output from the build stage
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FAST.Gate.dll"]
