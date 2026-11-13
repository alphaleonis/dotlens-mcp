set windows-shell := ["powershell", "-NoProfile", "-Command"]

# DotLens build tasks

project := "src/DotLens/DotLens.csproj"
tests := "tests/DotLens.Tests/DotLens.Tests.csproj"

# List available recipes
default:
    @just --list

# Build the project
build:
    dotnet build {{project}}

# Run all tests
test:
    dotnet test --project {{tests}}

# Build and test
check: build test

# Check code formatting (same as CI)
format-check:
    dotnet format --verify-no-changes

# Fix code formatting
format:
    dotnet format

# Generate THIRD-PARTY-NOTICES.txt from NuGet dependencies
notices:
    dotnet tool restore
    dotnet dotnet-project-licenses -i {{project}} --unique --json --outfile .notices-tmp.json
    dotnet run build/generate-notices.cs

# Build a Release NuGet package
pack:
    dotnet pack {{project}} --configuration Release --output ./artifacts
