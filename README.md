# Selpo.EET2.0

`Selpo.EET2.0` is a .NET connector for integrating applications with the Czech Ministry of Finance EET 2.0 service.

## Solution structure

- `src/Selpo.EET2.0` - the production connector library
- `tests/Selpo.EET2.0.Tests` - unit tests for the connector

## Supported target frameworks

- .NET Framework 4.8.1 (`net481`)
- .NET 10 (`net10.0`)

## Build and test

```bash
dotnet restore Selpo.EET2.0.sln
dotnet build Selpo.EET2.0.sln --configuration Release
dotnet test tests/Selpo.EET2.0.Tests/Selpo.EET2.0.Tests.csproj --configuration Release
```

## Create the NuGet package

```bash
dotnet pack src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
```

The resulting package contains assemblies for both `net481` and `net10.0`.

## Continuous integration and publishing

GitHub Actions builds the solution and runs the tests on pull requests. A tag such as `v0.1.0` additionally creates and publishes the package to NuGet.org. The repository must contain a `NUGET_API_KEY` GitHub Actions secret for publishing.

## Status

The project is currently in the initial development phase. The EET 2.0 protocol implementation will be added incrementally based on the official technical specification.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
