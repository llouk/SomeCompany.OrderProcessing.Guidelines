FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SomeCompany.OrderProcessing.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Non-root user shipped with the official image; listens on 8080 by default.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SomeCompany.OrderProcessing.Api.dll"]
