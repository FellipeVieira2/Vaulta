# Acesse https://aka.ms/customizecontainer para saber como personalizar seu contêiner de depuração e como o Visual Studio usa este Dockerfile para criar suas imagens para uma depuração mais rápida.

# Esta fase é usada durante a execução no VS no modo rápido (Padrão para a configuração de Depuração)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng tesseract-ocr-por && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# Esta fase é usada para compilar o projeto de serviço
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/Vaulta.Web.Api/Vaulta.Web.Api.csproj", "src/Vaulta.Web.Api/"]
COPY ["src/Modules/Identity/Vaulta.Identity.Infrastructure/Vaulta.Identity.Infrastructure.csproj", "src/Modules/Identity/Vaulta.Identity.Infrastructure/"]
COPY ["src/Modules/Identity/Vaulta.Identity.Application/Vaulta.Identity.Application.csproj", "src/Modules/Identity/Vaulta.Identity.Application/"]
COPY ["src/Modules/Identity/Vaulta.Identity.Domain/Vaulta.Identity.Domain.csproj", "src/Modules/Identity/Vaulta.Identity.Domain/"]
COPY ["src/Vaulta.SharedKernel/Vaulta.SharedKernel.csproj", "src/Vaulta.SharedKernel/"]
COPY ["src/Modules/Identity/Vaulta.Identity.Contracts/Vaulta.Identity.Contracts.csproj", "src/Modules/Identity/Vaulta.Identity.Contracts/"]
COPY ["src/Modules/Catalog/Vaulta.Catalog.Infrastructure/Vaulta.Catalog.Infrastructure.csproj", "src/Modules/Catalog/Vaulta.Catalog.Infrastructure/"]
COPY ["src/Modules/Catalog/Vaulta.Catalog.Application/Vaulta.Catalog.Application.csproj", "src/Modules/Catalog/Vaulta.Catalog.Application/"]
COPY ["src/Modules/Catalog/Vaulta.Catalog.Domain/Vaulta.Catalog.Domain.csproj", "src/Modules/Catalog/Vaulta.Catalog.Domain/"]
COPY ["src/Modules/Catalog/Vaulta.Catalog.Contracts/Vaulta.Catalog.Contracts.csproj", "src/Modules/Catalog/Vaulta.Catalog.Contracts/"]
COPY ["src/Modules/Assets/Vaulta.Assets.Infrastructure/Vaulta.Assets.Infrastructure.csproj", "src/Modules/Assets/Vaulta.Assets.Infrastructure/"]
COPY ["src/Modules/Assets/Vaulta.Assets.Application/Vaulta.Assets.Application.csproj", "src/Modules/Assets/Vaulta.Assets.Application/"]
COPY ["src/Modules/Assets/Vaulta.Assets.Domain/Vaulta.Assets.Domain.csproj", "src/Modules/Assets/Vaulta.Assets.Domain/"]
COPY ["src/Modules/Assets/Vaulta.Assets.Contracts/Vaulta.Assets.Contracts.csproj", "src/Modules/Assets/Vaulta.Assets.Contracts/"]
COPY ["src/Modules/Collection/Vaulta.Collection.Infrastructure/Vaulta.Collection.Infrastructure.csproj", "src/Modules/Collection/Vaulta.Collection.Infrastructure/"]
COPY ["src/Modules/Collection/Vaulta.Collection.Application/Vaulta.Collection.Application.csproj", "src/Modules/Collection/Vaulta.Collection.Application/"]
COPY ["src/Modules/Collection/Vaulta.Collection.Domain/Vaulta.Collection.Domain.csproj", "src/Modules/Collection/Vaulta.Collection.Domain/"]
COPY ["src/Modules/Collection/Vaulta.Collection.Contracts/Vaulta.Collection.Contracts.csproj", "src/Modules/Collection/Vaulta.Collection.Contracts/"]
COPY ["src/Modules/Marketplace/Vaulta.Marketplace.Infrastructure/Vaulta.Marketplace.Infrastructure.csproj", "src/Modules/Marketplace/Vaulta.Marketplace.Infrastructure/"]
COPY ["src/Modules/Marketplace/Vaulta.Marketplace.Application/Vaulta.Marketplace.Application.csproj", "src/Modules/Marketplace/Vaulta.Marketplace.Application/"]
COPY ["src/Modules/Marketplace/Vaulta.Marketplace.Domain/Vaulta.Marketplace.Domain.csproj", "src/Modules/Marketplace/Vaulta.Marketplace.Domain/"]
COPY ["src/Modules/Marketplace/Vaulta.Marketplace.Contracts/Vaulta.Marketplace.Contracts.csproj", "src/Modules/Marketplace/Vaulta.Marketplace.Contracts/"]
COPY ["src/Modules/Orders/Vaulta.Orders.Infrastructure/Vaulta.Orders.Infrastructure.csproj", "src/Modules/Orders/Vaulta.Orders.Infrastructure/"]
COPY ["src/Modules/Orders/Vaulta.Orders.Application/Vaulta.Orders.Application.csproj", "src/Modules/Orders/Vaulta.Orders.Application/"]
COPY ["src/Modules/Orders/Vaulta.Orders.Domain/Vaulta.Orders.Domain.csproj", "src/Modules/Orders/Vaulta.Orders.Domain/"]
COPY ["src/Modules/Orders/Vaulta.Orders.Contracts/Vaulta.Orders.Contracts.csproj", "src/Modules/Orders/Vaulta.Orders.Contracts/"]
COPY ["src/Modules/Payments/Vaulta.Payments.Infrastructure/Vaulta.Payments.Infrastructure.csproj", "src/Modules/Payments/Vaulta.Payments.Infrastructure/"]
COPY ["src/Modules/Payments/Vaulta.Payments.Application/Vaulta.Payments.Application.csproj", "src/Modules/Payments/Vaulta.Payments.Application/"]
COPY ["src/Modules/Payments/Vaulta.Payments.Domain/Vaulta.Payments.Domain.csproj", "src/Modules/Payments/Vaulta.Payments.Domain/"]
COPY ["src/Modules/Payments/Vaulta.Payments.Contracts/Vaulta.Payments.Contracts.csproj", "src/Modules/Payments/Vaulta.Payments.Contracts/"]
COPY ["src/Modules/Wallets/Vaulta.Wallets.Application/Vaulta.Wallets.Application.csproj", "src/Modules/Wallets/Vaulta.Wallets.Application/"]
COPY ["src/Modules/Wallets/Vaulta.Wallets.Contracts/Vaulta.Wallets.Contracts.csproj", "src/Modules/Wallets/Vaulta.Wallets.Contracts/"]
COPY ["src/Modules/Wallets/Vaulta.Wallets.Domain/Vaulta.Wallets.Domain.csproj", "src/Modules/Wallets/Vaulta.Wallets.Domain/"]
COPY ["src/Modules/Shipping/Vaulta.Shipping.Infrastructure/Vaulta.Shipping.Infrastructure.csproj", "src/Modules/Shipping/Vaulta.Shipping.Infrastructure/"]
COPY ["src/Modules/Shipping/Vaulta.Shipping.Application/Vaulta.Shipping.Application.csproj", "src/Modules/Shipping/Vaulta.Shipping.Application/"]
COPY ["src/Modules/Shipping/Vaulta.Shipping.Domain/Vaulta.Shipping.Domain.csproj", "src/Modules/Shipping/Vaulta.Shipping.Domain/"]
COPY ["src/Modules/Shipping/Vaulta.Shipping.Contracts/Vaulta.Shipping.Contracts.csproj", "src/Modules/Shipping/Vaulta.Shipping.Contracts/"]
COPY ["src/Modules/Reviews/Vaulta.Reviews.Infrastructure/Vaulta.Reviews.Infrastructure.csproj", "src/Modules/Reviews/Vaulta.Reviews.Infrastructure/"]
COPY ["src/Modules/Reviews/Vaulta.Reviews.Application/Vaulta.Reviews.Application.csproj", "src/Modules/Reviews/Vaulta.Reviews.Application/"]
COPY ["src/Modules/Reviews/Vaulta.Reviews.Domain/Vaulta.Reviews.Domain.csproj", "src/Modules/Reviews/Vaulta.Reviews.Domain/"]
COPY ["src/Modules/Reviews/Vaulta.Reviews.Contracts/Vaulta.Reviews.Contracts.csproj", "src/Modules/Reviews/Vaulta.Reviews.Contracts/"]
COPY ["src/Modules/Wallets/Vaulta.Wallets.Infrastructure/Vaulta.Wallets.Infrastructure.csproj", "src/Modules/Wallets/Vaulta.Wallets.Infrastructure/"]
RUN dotnet restore "./src/Vaulta.Web.Api/Vaulta.Web.Api.csproj"
COPY . .
WORKDIR "/src/src/Vaulta.Web.Api"
RUN dotnet build "./Vaulta.Web.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS scanner-tests
WORKDIR /src
RUN dotnet test tests/Vaulta.App.Core.UnitTests/Vaulta.App.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~ScannerClientTests --verbosity minimal
RUN dotnet test tests/Vaulta.Identity.UnitTests/Vaulta.Identity.UnitTests.csproj -c Release --filter "FullyQualifiedName~TesseractOcrTests|FullyQualifiedName~ScannerPricingTests|FullyQualifiedName~FuzzyCardRecognitionTests" --verbosity minimal

# Esta fase é usada para publicar o projeto de serviço a ser copiado para a fase final
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Vaulta.Web.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Esta fase é usada na produção ou quando executada no VS no modo normal (padrão quando não está usando a configuração de Depuração)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vaulta.Web.Api.dll"]
