FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SportsBookingService.Api/SportsBookingService.Api.csproj src/SportsBookingService.Api/
RUN dotnet restore src/SportsBookingService.Api/SportsBookingService.Api.csproj

COPY src/SportsBookingService.Api/ src/SportsBookingService.Api/
RUN dotnet publish src/SportsBookingService.Api/SportsBookingService.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
CMD ASPNETCORE_URLS=http://+:$PORT dotnet SportsBookingService.Api.dll
