# postgres:17-alpine plus pgvector. Stays on Alpine (musl) on purpose: switching an existing pgdata volume to a
# Debian (glibc) image changes text collation and silently corrupts text indexes.
FROM postgres:17-alpine

ARG PGVECTOR_VERSION=v0.8.0

# with_llvm=no: skip JIT bitcode (avoids needing the exact clang/llvm the base image was built with).
# OPTFLAGS="": no -march=native, so the image runs on any x86-64 NAS CPU, not just the build machine.
RUN apk add --no-cache --virtual .build-deps git build-base \
    && git clone --branch "${PGVECTOR_VERSION}" --depth 1 https://github.com/pgvector/pgvector.git /tmp/pgvector \
    && cd /tmp/pgvector \
    && make OPTFLAGS="" with_llvm=no \
    && make install with_llvm=no \
    && cd / && rm -rf /tmp/pgvector \
    && apk del .build-deps
