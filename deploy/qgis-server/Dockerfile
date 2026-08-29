FROM debian:bookworm-slim

ENV LANG=en_US.UTF-8 DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install --no-install-recommends -y ca-certificates gnupg locales spawn-fcgi tini wget xvfb \
    && localedef -i en_US -f UTF-8 en_US.UTF-8 \
    && mkdir -p /etc/apt/keyrings \
    && wget -qO /etc/apt/keyrings/qgis-archive-keyring.gpg https://download.qgis.org/downloads/qgis-archive-keyring.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/qgis-archive-keyring.gpg] https://qgis.org/debian-ltr bookworm main" > /etc/apt/sources.list.d/qgis.sources.list \
    && apt-get update \
    && apt-get install --no-install-recommends -y qgis-server fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --uid 10002 --create-home qgis

COPY start-qgis-server.sh /usr/local/bin/start-qgis-server
RUN chmod 0755 /usr/local/bin/start-qgis-server
USER qgis
WORKDIR /home/qgis
EXPOSE 5555
ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["/usr/local/bin/start-qgis-server"]
