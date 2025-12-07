FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SeatGrid.PaymentService/SeatGrid.PaymentService.csproj", "src/SeatGrid.PaymentService/"]
COPY ["src/SeatGrid.Contracts/SeatGrid.Contracts.csproj", "src/SeatGrid.Contracts/"]
RUN dotnet restore "./src/SeatGrid.PaymentService/SeatGrid.PaymentService.csproj"
COPY . .
WORKDIR "/src/src/SeatGrid.PaymentService"
RUN dotnet build "./SeatGrid.PaymentService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SeatGrid.PaymentService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SeatGrid.PaymentService.dll"]
