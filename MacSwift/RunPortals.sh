#!/bin/bash

cd "$HOME/Development/Swift Portals" || exit 1

echo "Compiling Portals..."

if swiftc main.swift PortalStore.swift -o PortalTest -framework AppKit -framework CoreGraphics; then
    echo "Compilation successful."
    echo "Starting Portals..."
    echo "Running Portals from: $(pwd)"
    ./PortalTest
else
    echo "Compilation failed. Please check the errors above."
fi
