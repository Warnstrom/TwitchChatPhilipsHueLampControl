#!/bin/bash

# Run the dotnet publish command
dotnet publish -c Release -r linux-x64 --self-contained true -o ./bin/Release/publish_linux-x64

# Navigate to the output directory
cd ./bin/Release/publish_linux-x64

# Run the zip command to compress the contents
zip -r ../publish_linux.zip *

# Run the dotnet publish command
dotnet publish -c Release -r win-x64 --self-contained true -o ./bin/Release/publish_windows-x64

# Navigate to the output directory
cd ./bin/Release/publish_windows-x64

# Run the zip command to compress the contents
zip -r ../publish_windows.zip *

#dotnet publish -c Release -r linux-x64 --self-contained -o ./bin/Release/net8.0/linux-x64/publish/
# Test6