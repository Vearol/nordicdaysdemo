FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine as builder
WORKDIR /app
COPY Backend.csproj /app/
RUN dotnet restore

COPY . /app/.
RUN dotnet publish -c Release -o /app/published

FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine
COPY --from=builder /app/published /app
EXPOSE 80
WORKDIR /app
ENTRYPOINT ["dotnet", "Backend.dll"]