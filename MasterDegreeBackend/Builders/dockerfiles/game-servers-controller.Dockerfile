FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY GameServersController/*.csproj ./GameServersController/
COPY GameServersControllerPlugin/*.csproj ./GameServersControllerPlugin/
COPY GameLogicCommon/*.csproj ./GameLogicCommon/
RUN dotnet restore ./GameServersController/ -v n

# Copy everything else, test and build
COPY GameServersController ./GameServersController
COPY GameServersControllerPlugin ./GameServersControllerPlugin
COPY GameLogicCommon ./GameLogicCommon
COPY DarkRift ./DarkRift
ARG build_configuration=Release
RUN dotnet publish -c ${build_configuration} -o ./GameServersController/out ./GameServersController

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/GameServersController/out .

RUN mkdir "Plugins"
RUN cp GameServersControllerPlugin.dll Plugins
RUN cp GameServersControllerPlugin.pdb Plugins   

ENV KUBERNETES_SERVICE_HOST kubernetes.default.svc
ENV KUBERNETES_SERVICE_PORT 443
ENV MANUAL false

EXPOSE 39998/tcp
EXPOSE 39998/udp
ENTRYPOINT ["dotnet", "GameServersController.dll"]