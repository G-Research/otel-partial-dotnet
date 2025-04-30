# otel-partial-dotnet
OTEL Dotnet SDK extension supporting partial spans

# install
[![NuGet](https://img.shields.io/nuget/vpre/G-Research.OpenTelemetry.Processor.Partial)](https://www.nuget.org/packages/G-Research.OpenTelemetry.Processor.Partial/absoluteLatest)

```bash
dotnet add package G-Research.OpenTelemetry.Processor.Partial
```

# build
```bash
dotnet build
```

# test
```bash
dotnet test
```

# usage
Check `Example.cs` for usage examples.

## publishing
NuGet is published to NuGet Gallery via the `ci.yml` GitHub Action workflow (approval required).
The publishing part of the workflow is triggered when a new tag is pushed to the repository. Only tags with the format `vX.Y.Z` will trigger the workflow. It's the responsibility of the approver to check that the tag points to a commit on the `master` branch and that its name matches the version in `G-Research.OpenTelemetry.Processor.Partial.csproj`.

Checklist:
- Bump the version in `G-Research.OpenTelemetry.Processor.Partial.csproj` via pull request
- Create a tag on `master` with the format `vX.Y.Z` and push it
- Review the workflow approval request - the tag should point to a commit on the `main` branch!
- Success

Link to NuGet Gallery: https://www.nuget.org/packages/G-Research.OpenTelemetry.Processor.Partial