
#!/bin/bash

cd ~/Development/PortalsMacTest || exit 1

echo "Compiling Portals..."
swiftc main.swift PortalStore.swift -o PortalTest -framework AppKit -framework CoreGraphics

if [ $? -eq 0 ]; then
    echo "Compilation successful."
    echo "Starting Portals..."
    ./PortalTest
else
    echo "Compilation failed. Please check the errors above."
fi

