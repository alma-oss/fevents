Events
======

[![NuGet](https://img.shields.io/nuget/v/Alma.Events.svg)](https://www.nuget.org/packages/Alma.Events)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Alma.Events.svg)](https://www.nuget.org/packages/Alma.Events)
[![Tests](https://github.com/alma-oss/fevents/actions/workflows/tests.yaml/badge.svg)](https://github.com/alma-oss/fevents/actions/workflows/tests.yaml)

Event types and modules with definitions for generic event sourcing.

## Install

Add following into `paket.references`
```
Alma.Events
```

## Release
1. Increment version in `Events.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)

### Build
```bash
./build.sh build
```

### Tests
```bash
./build.sh -t tests
```
