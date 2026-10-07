FROM almalinux:8.10@sha256:8b469a3a78515e8a18ea8fc727e6a3679e1d0c5ba6f58d5b30be3d0d9b86cffe
COPY packages.txt /opt/packages.txt
RUN dnf --enablerepo=powertools install -y $(cat /opt/packages.txt) \
    && rpm -qa --qf '%{NAME}-%{EPOCHNUM}:%{VERSION}-%{RELEASE}.%{ARCH}\n' | sort > /opt/installed-rpms.txt \
    && dnf clean all
COPY sdk.tar.gz sdk.sha512 cmake.tar.gz cmake.sha256 /tmp/
RUN cd /tmp && sha512sum -c sdk.sha512 && sha256sum -c cmake.sha256 \
    && mkdir -p /opt/dotnet /opt/cmake \
    && tar xzf sdk.tar.gz -C /opt/dotnet \
    && tar xzf cmake.tar.gz -C /opt/cmake --strip-components=1 \
    && rm sdk.tar.gz cmake.tar.gz
ENV PATH="/opt/dotnet:/opt/cmake/bin:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /repo
