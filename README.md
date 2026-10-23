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

## CloudEvents

Two ways to build an envelope ([CloudEvents v1.0](https://github.com/cloudevents/spec/blob/v1.0/spec.md)):

- **strict** (`CloudEvent.create`): `id` and `time` come from the common event, `source` is an absolute URI, `dataschema` is required. The original, internal policy.
- **spec** (`CloudEvent.createFromEnvelope`): only `id`, `type` and `source` are required; `source` is any URI-reference (for example the emitting software component `instance@environment`), `subject`, `dataschema`, `time` and extension attributes are optional.

```fsharp
open System
open Alma.Events
open Alma.EnvironmentModel
open Alma.Kafka
open Alma.ServiceIdentification
open Feather.ErrorHandling

type IncidentData = { Id: string; Severity: string }

let instance = Instance.parseStrict "-" "alma-status-prod-stable" |> Result.orFail
let environment = Environment.parse "dev1-services" |> Result.orFail
let recordedTime = ExtensionName.create "recordedtime" |> Result.orFail

let envelope =
    Envelope.create (EventId (Guid "01a16833-fd00-7000-8000-042b88a88410")) (EventName "status.incident.started") (EventSource.SoftwareComponent (Untyped (instance, environment)))
    |> Envelope.withSubject "web-client/2026-10-23T09:59:40Z"
    |> Envelope.withTime (DateTimeOffset.Parse "2026-10-23T09:59:40Z")
    |> Envelope.withExtension recordedTime (ExtensionValue.Timestamp (DateTimeOffset.Parse "2026-10-23T10:00:00Z"))

let json =
    CloudEvent.createFromEnvelope envelope { Id = "web-client/2026-10-23T09:59:40Z"; Severity = "critical" }
    |> CloudEvent.toJson []
```

```json
{
  "specversion": "1.0",
  "id": "01a16833-fd00-7000-8000-042b88a88410",
  "source": "alma-status-prod-stable@dev1-services",
  "type": "status.incident.started",
  "datacontenttype": "application/json",
  "subject": "web-client/2026-10-23T09:59:40Z",
  "time": "2026-10-23T09:59:40Z",
  "recordedtime": "2026-10-23T10:00:00Z",
  "data": { "id": "web-client/2026-10-23T09:59:40Z", "severity": "critical" }
}
```

`CloudEvent.parse` reads both forms; `CloudEvent.tryExtension` and `CloudEvent.tryData` read an extension attribute (as a string after a parse) and the typed data back.

```fsharp
// parseIncident: string -> Result<IncidentData, 'Error> parses the `data` attribute
let parsed = json |> CloudEvent.parse parseIncident |> Result.orFail

parsed |> CloudEvent.tryExtension recordedTime // Some (ExtensionValue.String "2026-10-23T10:00:00Z")
parsed |> CloudEvent.tryData<IncidentData>     // Some { Id = "web-client/2026-10-23T09:59:40Z"; Severity = "critical" }
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
