---
name: fevents
description: Use whenever generating or reviewing F# code that parses, transforms, serializes, or validates events with the Alma.Events library — e.g. calling CloudEvent.parse / CloudEvent.create / CloudEvent.mapData / CloudEvent.toJson, implementing a Parse<'Event>, composing Transform.toPublic, building DTOs with DtoError / Serialize helpers, reading RawJson, or validating with JsonSchema. Trigger also on mentions of CloudEvents envelopes, EventSource, DataSchema, ParseError, ProcessedBy metadata, RawEvent, or Alma.Events.TestsUtils JSON-normalizing test assertions.
---

# F-Events

Library: [alma-oss/fevents](https://github.com/alma-oss/fevents)
NuGet: `Alma.Events`

## Purpose

`Alma.Events` is an F# library for generic event sourcing. It parses raw events
into typed events, transforms inbound events into public events, serializes them
through DTOs, and wraps them in CloudEvents v1.0 envelopes. It also provides JSON
schema validation and test utilities for downstream projects.

## When to Use

- Writing a `Parse<'Event>` to turn a `RawEvent` into a typed event.
- Wrapping or unwrapping domain events in CloudEvents envelopes.
- Transforming an inbound event into a public event (new id, causation chain).
- Serializing events through DTOs and rendering `ParseError` / `DtoError`.
- Validating JSON payloads against a schema, or asserting serialized JSON in tests.

## When NOT to Use

- Defining business/domain events themselves (those live in consuming services).
- Kafka production/consumption mechanics — that is `Alma.Kafka`'s responsibility.
- Generic JSON serialization unrelated to the event pipeline (use `Alma.Serializer`).

## Main Concepts

- **`CloudEvent`** — module to `create`, `parse`, `mapData`/`mapDataResult`,
  `toJson`, `toHttpContent` CloudEvents v1.0 envelopes.
- **`EventSource` / `DataSchema` / `ContentType`** — single-case DU wrappers for
  envelope metadata (absolute URIs / `application/json`).
- **`Parse<'Event>`** — alias `RawEvent -> Result<'Event, ParseError>`.
- **`ParseError`** — inbound parsing error DU; CloudEvent parsing has its own
  `ParseError<'ParseEventError>`. Render with `ParseError.format`.
- **`Transform.toPublic`** — promotes an inbound event to a public event with a
  new `EventId`, derived `CausationId`, and processed metadata.
- **`MetaData`** — `OnlyCreatedAt` vs `CreatedAndProcessed`; `requireProcessedBy`.
- **`DtoError` / `Serialize` / `ToDto`** — outbound serialization through DTOs.
- **`RawJson`** — abstraction over FSharp.Data `JsonValue` and `JsonElement`.
- **`JsonSchema`** — `parseSchema` + `assertJsonSchemaMatch` (NJsonSchema).
- **`TestsUtils`** — JSON-normalizing assertions for downstream test projects.

## Related Libraries

- **Alma.Kafka** — core event vocabulary (`Event`, `EventId`, `EventName`,
  `CausationId`, `MetaData`, `RawEvent`, `ProcessedBy`).
- **Feather.ErrorHandling** — `result { }` / `asyncResult { }` computation
  expressions used throughout.
- **Alma.Serializer** — serializer options for envelope output.
- **CloudNative.CloudEvents** — the underlying `CloudEvent` type.
- **FSharp.Data** — `JsonProvider` for compile-time event parsing.

## Keywords for Search

Alma.Events, fevents, CloudEvent, CloudEvents, cloud event envelope, EventSource, DataSchema, ContentType, ParseError, Parse, RawEvent, Transform.toPublic, public event, CausationId, ProcessedBy, MetaData, requireProcessedBy, DtoError, SerializeDto, ToDto, RawJson, JsonValue, JsonElement, JsonSchema, NJsonSchema, assertJsonSchemaMatch, TestsUtils, assertJsonEquals, normalizeId, normalizeCreatedAt, Alma.Kafka, Feather.ErrorHandling, F# event sourcing

## Reference Files

- For composition principles and recommended API usage, read `references/preferred-patterns.md`.
- For known pitfalls and incorrect assumptions, read `references/anti-patterns.md`.
- For worked code examples, read `references/examples.md`.
