# Validating invoices in a container, with the rule artefacts baked into the image.
#
# The point of this file is the second RUN in the build stage: the artefacts are fetched once, at
# build time, and copied into the runtime image. The container never reaches the network to
# validate, and the image is reproducible — the embedded manifest pins every artefact by SHA-256,
# so a build either got exactly the bytes the manifest names or it failed.
#
#   python tools/verify-package.py                       # produces artifacts/*.nupkg
#   docker build -t klarfakt-quickstart \
#     --build-context feed=artifacts \
#     -f samples/Quickstart/Dockerfile samples/Quickstart
#   docker run --rm klarfakt-quickstart
#
# The feed context is only needed while the package is unpublished. Once it is on nuget.org, point
# it at any empty directory, or delete both /feed lines and the --source flag.

ARG VERSION=1.0.0-preview.1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG VERSION
WORKDIR /src

COPY --from=feed . /feed
COPY . .

RUN dotnet publish --configuration Release \
        -p:KlarfaktVersion=$VERSION \
        --source /feed --source https://api.nuget.org/v3/index.json \
        --output /app

# The rule packs. KLARFAKT_RULES decides where they land; the runtime stage reads the same path.
# "rules verify" is not redundant: restore checks what it downloads, verify checks what is on disk.
RUN dotnet tool install --tool-path /tools Klarfakt.Cli --version $VERSION --add-source /feed \
 && KLARFAKT_RULES=/rules /tools/klarfakt rules restore \
 && KLARFAKT_RULES=/rules /tools/klarfakt rules verify

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

COPY --from=build /app .
COPY --from=build /rules /rules

# Nothing in Klarfakt touches the network once the artefacts are present, so this image validates
# offline. It runs as a non-root user and needs no writable filesystem.
ENV KLARFAKT_RULES=/rules
USER $APP_UID

ENTRYPOINT ["dotnet", "Quickstart.dll"]
