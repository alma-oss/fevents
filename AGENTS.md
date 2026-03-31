# Alma.Events (fevents)

Open-source F# library (`Alma.Events` NuGet package) providing event types, parsing, serialization, CloudEvents support, and JSON schema validation for the Alma platform's event-driven architecture. Used by downstream services to define, produce, consume, and validate domain events over Kafka following the CloudEvents v1.0 specification.

## Tech Stack

- **Language:** F# (.NET 10.0)
- **Build system:** FAKE (F# Make) via `build.sh` wrapper
- **Package management:** Paket (`paket.dependencies` / `paket.references`)
- **Test framework:** Expecto
- **Linter:** FSharpLint (`fsharplint.json`)
- **CI/CD:** GitHub Actions
- **Key dependencies:**
  - `FSharp.Core` ~> 10.0
  - `FSharp.Data` ~> 6.0 (JSON type providers for schema-based parsing)
  - `NJsonSchema` ~> 11.0 (runtime JSON schema validation)
  - `CloudNative.CloudEvents` ~> 2.8 (CloudEvents SDK — core types)
  - `CloudNative.CloudEvents.NewtonsoftJson` ~> 2.8 (CloudEvents JSON serialization)
  - `Feather.ErrorHandling` ~> 2.0 (Result/AsyncResult computation expressions)
  - `Alma.Kafka` ~> 30.0 (Kafka event types: `Event`, `EventId`, `EventName`, `MetaData`, `RawEvent`, etc.)
  - `Alma.Serializer` ~> 9.0 (JSON serialization utilities)

## Commands

```bash
# Restore tools + packages and build
./build.sh build

# Run tests
./build.sh -t tests

# Lint
./build.sh -t lint

# Pack NuGet package
./build.sh -t release

# Publish to NuGet.org (requires NUGET_API_KEY)
./build.sh -t publish
```

`build.sh` runs: `dotnet tool restore` → `dotnet tool run paket restore` → FAKE build pipeline.

## Project Structure

```
Events.fsproj           # Library project (OutputType: Library)
AssemblyInfo.fs         # Auto-generated assembly metadata

src/
  Parser.fs             # CommonParser (date parsing), Utils (String, Regex active patterns)
  JsonSchema.fs         # JSON schema validation using NJsonSchema
                        #   (parseSchema, assertJsonSchemaMatch)
  CommonTypes.fs        # Core event types: ParseError, Parse<'Event>, MetaData helpers,
                        #   Transform (toPublic), EventType assertion,
                        #   DtoError, Serialize helpers, ToDto, RawJson
  CloudEvent.fs         # CloudEvents v1.0 integration:
                        #   ContentType, EventSource, DataSchema,
                        #   CloudEvent.create (wraps domain event in CloudEvent),
                        #   CloudEvent.parse (deserializes from JSON),
                        #   CloudEvent.mapData / mapDataResult,
                        #   CloudEvent.toHttpContent / toJson
  TestsUtils.fs         # Test utilities: assertJsonEquals, AssertSerializedEvent
                        #   normalizers (GUIDs, timestamps), JsonSchemaValidation

  schema/
    cloudEvent.json     # JSON sample for FSharp.Data type provider (CloudEvent parsing)

tests/
  Tests.fs              # Expecto test entry point
  CloudEvents.fs        # CloudEvent serialization/parsing tests
  SerializingEvents.fs  # Event serialization tests
  Fixtures/             # Test fixture data

build/
  Build.fs              # FAKE build project definition
  Targets.fs            # FAKE targets (Clean, Build, Lint, Tests, Release, Publish)
  Utils.fs              # Build utility functions
  SafeBuildHelpers.fs   # SAFE Stack build helpers (not used in this library)
```

## Architecture & Domain Concepts

### Event Model (from Alma.Kafka)
Events follow a generic structure: `Event<'KeyData, 'MetaData, 'DomainData>` with fields for Schema, Id, CorrelationId, CausationId, Timestamp, Event name, Domain/Context/Purpose/Version/Zone/Bucket (service identification), Resource, MetaData, KeyData, and DomainData.

### CloudEvents Integration
Domain events are wrapped in CloudEvents v1.0 envelopes for interoperability:
- `CloudEvent.create` — wraps a domain event into a `CloudNative.CloudEvents.CloudEvent`
- `CloudEvent.parse` — deserializes a raw JSON string back to a CloudEvent, validating specversion, content type, source, and data schema
- `CloudEvent.toJson` / `toHttpContent` — serializes for transport

### Event Transformation Pipeline
1. **Parse** raw Kafka event → typed `Event<'KeyData, 'MetaData, 'DomainData>`
2. **Transform** to public event (new ID, causation chain, processed metadata) via `Transform.toPublic`
3. **Serialize** to DTO → JSON string
4. Optionally wrap in CloudEvent envelope

### JSON Schema Validation
`JsonSchema` module provides runtime validation of JSON against NJsonSchema schemas — used to validate event payloads before publishing.

### MetaData
- `OnlyCreatedAt` — events with just a creation timestamp
- `CreatedAndProcessed` — events that have been processed (includes `ProcessedBy`)
- `MetaData.requireProcessedBy` — asserts an event has processing metadata

## Conventions

- **Single-case DU wrappers** for domain primitives (`EventSource of Uri`, `DataSchema of Uri`, `ContentType`)
- **`[<RequireQualifiedAccess>]`** on all modules
- **Result-based error handling** — `Feather.ErrorHandling` computation expressions (`result { }`, `asyncResult { }`)
- **JSON type providers** (`FSharp.Data.JsonProvider`) for compile-time schema-based parsing — schemas in `src/schema/`
- **DTO pattern**: Domain type → DTO type → serialized JSON
- **`Parse<'Event>`** type alias: `RawEvent -> Result<'Event, ParseError>`
- **`Serialize<'Event, ...>`** type alias: `SerializeDto -> 'Event -> Result<string, DtoError<...>>`
- **Test utilities** (`TestsUtils.fs`) are part of the library — they are intended for use in downstream service tests
- **No mutable state** — all types are immutable records or DUs

## CI/CD Workflows

| Workflow | Trigger | What it does |
|----------|---------|-------------|
| `tests.yaml` | PR, daily cron (3 AM) | Runs `./build.sh -t tests` on ubuntu-latest with .NET 10.x |
| `pr-check.yaml` | PR | Blocks fixup commits, runs ShellCheck on shell scripts |
| `publish.yaml` | Tag push (`X.Y.Z`) | Runs `./build.sh -t publish` to publish NuGet package |

## Release Process

1. Increment `<Version>` in `Events.fsproj`
2. Update `CHANGELOG.md` (move items from Unreleased to new version section)
3. Commit and push
4. Create a git tag matching the version (e.g., `7.0.0`) — this triggers the publish workflow

## Pitfalls

- **JSON schema files are required at compile time** — `FSharp.Data.JsonProvider` uses `src/schema/cloudEvent.json` as a compile-time sample. The `cloudEvent.json` is also included as `Content` in the fsproj with `CopyToOutputDirectory`. Do not move or rename without updating both the type provider path and fsproj item.
- **No docker-compose** — this is a library, not a service. No local environment to spin up.
- **Paket, not NuGet CLI** — always use `dotnet paket install` to add packages, not `dotnet add package`.
- **FAKE build system** — the entry point is `build.sh`, not `dotnet build` directly (though `dotnet build` works for compilation).
- **`TestsUtils.fs`** is compiled into the library itself (not just in tests) — it is intentionally part of the public API for downstream test projects.
- **`Alma.*` packages** are internal/OSS Alma ecosystem packages — check their repos for API docs.
- **CloudEvents SDK** — the library wraps `CloudNative.CloudEvents` types. When modifying CloudEvent handling, refer to the CloudEvents v1.0 spec.
