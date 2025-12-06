# Use the official .NET 8.0 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the solution file and project files
COPY StartUply.sln ./
COPY StartUply.Application/StartUply.Application.csproj StartUply.Application/
COPY StartUply.Domain/StartUply.Domain.csproj StartUply.Domain/
COPY StartUply.Infrastructure/StartUply.Infrastructure.csproj StartUply.Infrastructure/
COPY StartUply.Presentation/StartUply.Presentation.csproj StartUply.Presentation/

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Build and publish the application
WORKDIR /src/StartUply.Presentation
RUN dotnet publish -c Release -o /app/publish

# Use the official .NET 8.0 ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose the port (Render will set PORT env var)
EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "StartUply.Presentation.dll"]