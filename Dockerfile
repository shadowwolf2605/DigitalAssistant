# Сглобява и пуска приложението в контейнер (за качване в интернет, например в Render)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DigitalAssistant/DigitalAssistant.csproj DigitalAssistant/
RUN dotnet restore DigitalAssistant/DigitalAssistant.csproj
COPY DigitalAssistant/ DigitalAssistant/
RUN dotnet publish DigitalAssistant/DigitalAssistant.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Българско време за датите. Администрацията е на същия адрес под /Admin,
# но се отваря само от мрежите в AdminSite__AllowedNetworks (вижте README).
ENV TZ=Europe/Sofia \
    ASPNETCORE_URLS=http://0.0.0.0:10000 \
    AdminSite__SameAddress=true
EXPOSE 10000
ENTRYPOINT ["dotnet", "DigitalAssistant.dll"]
