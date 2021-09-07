FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY GameServer/*.csproj ./GameServer/
COPY GameLogicPlugin/*.csproj ./GameLogicPlugin/
COPY GameLogicCommon/*.csproj ./GameLogicCommon/
RUN dotnet restore ./GameServer/ -v n

# Copy everything else, test and build
COPY GameServer ./GameServer
COPY GameLogicPlugin ./GameLogicPlugin
COPY GameLogicCommon ./GameLogicCommon
COPY DarkRift ./DarkRift
COPY Box2D.NetStandard ./Box2D.NetStandard
COPY SharpBox2d ./SharpBox2d
ARG build_configuration=Release
RUN dotnet publish -c ${build_configuration} -o ./GameServer/out ./GameServer

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/GameServer/out .

RUN mkdir "Plugins"
RUN cp GameLogicPlugin.dll Plugins
RUN cp GameLogicPlugin.pdb Plugins   

ENV MANUAL false

EXPOSE 39999/tcp
EXPOSE 39999/udp
ENTRYPOINT ["dotnet", "GameServer.dll"]