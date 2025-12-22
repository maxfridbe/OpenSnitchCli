#!/bin/bash
VERSION=$1
if [ -z "$VERSION" ]; then VERSION="1.0.0"; fi

fpm -s dir -t rpm \
    -n opensnitch-cli \
    -v ${VERSION} \
    --iteration 1 \
    -a x86_64 \
    -d "socat" \
    --description "Modern C# CLI for OpenSnitch" \
    --prefix /usr/bin \
    -p /dist/ \
    OpenSnitchCli
