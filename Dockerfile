FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /source

COPY src/Nadeko.Medusa/*.csproj src/Nadeko.Medusa/
COPY src/Nadeko.Econ/*.csproj src/Nadeko.Econ/
COPY src/Nadeko.Common/*.csproj src/Nadeko.Common/
COPY src/NadekoBot/*.csproj src/NadekoBot/
COPY src/NadekoBot.Coordinator/*.csproj src/NadekoBot.Coordinator/
COPY src/NadekoBot.Generators/*.csproj src/NadekoBot.Generators/
COPY src/ayu/Ayu.Discord.Voice/*.csproj src/ayu/Ayu.Discord.Voice/
COPY NuGet.Config ./
RUN dotnet restore src/NadekoBot/

COPY . .
WORKDIR /source/src/NadekoBot
RUN set -xe
RUN dotnet --version
RUN dotnet publish -c Release -o /app --no-restore
RUN mv /app/data /app/data_init
RUN rm -Rf libopus* libsodium* opus.* runtimes/win* runtimes/osx* runtimes/linux-arm* runtimes/linux-mips*
RUN find /app -type f -exec chmod -x {} \;
RUN chmod +x /app/NadekoBot

# final stage/image
FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /app

RUN set -xe
RUN useradd -m nadeko
RUN apt-get update
RUN apt-get install -y --no-install-recommends libopus0 libsodium23 libsqlite3-0 curl ffmpeg python3 python3-pip sudo
RUN update-alternatives --install /usr/bin/python python /usr/bin/python3 1
RUN echo 'Defaults>nadeko env_keep+="ASPNETCORE_* DOTNET_* NadekoBot_* shard_id total_shards TZ"' > /etc/sudoers.d/nadeko
RUN pip3 install --no-cache-dir --upgrade youtube-dl yt-dlp
RUN apt-get purge -y python3-pip
RUN chmod +x /usr/local/bin/youtube-dl
RUN chmod +x /usr/local/bin/yt-dlp
RUN apt-get autoremove -y
RUN apt-get autoclean -y

RUN mkdir /.cache
RUN chmod 777 /.cache

COPY --from=build /app ./
COPY docker-entrypoint.sh /usr/local/sbin

ENV shard_id=0
ENV total_shards=1
ENV NadekoBot__creds=/app/data/creds.yml

VOLUME [ "/app/data" ]
ENTRYPOINT [ "/usr/local/sbin/docker-entrypoint.sh" ]
CMD dotnet NadekoBot.dll "$shard_id" "$total_shards"
