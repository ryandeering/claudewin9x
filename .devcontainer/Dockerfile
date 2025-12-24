FROM mcr.microsoft.com/devcontainers/base:ubuntu

RUN apt-get update && apt-get install -y \
    # MinGW cross-compiler
    mingw-w64 \
    # Static analysis
    cppcheck \
    # Native AOT prerequisites for .NET
    clang \
    zlib1g-dev \
    # Build tools
    make \
    && rm -rf /var/lib/apt/lists/*
