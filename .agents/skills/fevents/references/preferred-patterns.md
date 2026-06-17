# Preferred Patterns

Recommended ways to use `Alma.Events`. Code is not duplicated here — each
pattern points to a named example in `examples.md`.

## Core Principles

- Model the flow as an explicit pipeline: **parse → transform → serialize →
  (optionally) wrap in a CloudEvent envelope**. Each stage is a pure function
  returning a `Result`.
- Domain primitives are single-case DU wrappers (`EventSource`, `DataSchema`,
  `BrowserInfo`, `IpAddress`, `Remark`, `Source`). Construct via the module's
  `tryParse`/`create` and unwrap via `value` — do not pattern-match inline.
- All modules are `[<RequireQualifiedAccess>]`. Always call them qualified
  (`CloudEvent.parse`, `Transform.toPublic`, `ParseError.format`).
- Type aliases (`Parse<'Event>`, `Serialize<...>`, `Transform<...>`, `ToDto<...>`,
  `Key<'Event>`) document the shape of the function you must supply. Annotate
  your functions with them so the compiler enforces the contract.

## Recommended API Usage

- **Parsing into typed events**: implement a `Parse<'Event>`
  (`RawEvent -> Result<'Event, ParseError>`). See `examples.md` → Example 2.
- **CloudEvents inbound**: `CloudEvent.parse parseEvent rawJson` validates
  specversion (`1.0`), content type (`application/json`), source, and data
  schema before invoking your `parseEvent`. See `examples.md` → Example 3.
- **CloudEvents outbound**: build the envelope with `CloudEvent.create`, then
  serialize with `CloudEvent.toJson` (string) or `CloudEvent.toHttpContent`
  (HTTP body). See `examples.md` → Examples 4 and 3.
- **Changing the carried payload**: `CloudEvent.mapData` for total transforms,
  `CloudEvent.mapDataResult` for fallible ones. See `examples.md` → Example 5.
- **Promoting an inbound event to a public event**: `Transform.toPublic`. It
  assigns a new `EventId`, derives `CausationId` from the source event's `Id`,
  and stamps processed metadata. See `examples.md` → Example 6.
- **Schema validation**: `JsonSchema.parseSchema` then
  `JsonSchema.assertJsonSchemaMatch`. Both return `AsyncResult`. See
  `examples.md` → Example 9.

## Error Handling

- Every fallible operation returns `Result`/`AsyncResult` — compose with
  `Feather.ErrorHandling` (`result { }`, `asyncResult { }`).
- Two error DUs exist for two stages: `ParseError` (inbound parsing) and
  `DtoError<...>` (outbound serialization). `CloudEvent` parsing has its own
  `ParseError<'ParseEventError>` that wraps your parser's error in
  `EventParseError`.
- Render any of them for logs with the matching `format` function
  (`ParseError.format`, `DtoError.format`,
  `JsonSchema.JsonSchemaValidationError.format`). Do not hand-roll messages.
- Use `EventType.assertSame` / `DtoError.assertEventType` to fail fast when an
  event's name does not match what the handler expects.

## Composition

- Chain stages with `Result.map` / `Result.mapError`; collapse the final error
  to a string with the relevant `format` at the edge. See `examples.md` →
  Example 11 for the complete pipeline.
- Keep `toCommon`, `subject`, `toDto`, and `transformDomainData` as small
  injected functions so the pipeline stays generic over event types.

## Integration with Other Libraries

- **Alma.Kafka** supplies the core event vocabulary (`Event<'K,'M,'D>`,
  `EventId`, `EventName`, `CausationId`, `MetaData`, `RawEvent`, `ProcessedBy`,
  `MessageKey`). `Alma.Events` builds on these — import both.
- **CloudNative.CloudEvents** provides the `CloudEvent` type returned by
  `CloudEvent.parse`/`create`; serialization uses the Newtonsoft formatter.
- **Alma.Serializer** provides the serializer options passed to
  `CloudEvent.toJson`/`toHttpContent` (e.g. `Serialize.Pretty`) and the
  `RawJson.toSerializableJsonIgnoringNullsInRecord` helper backing.
- **FSharp.Data** `JsonProvider` is the idiomatic way to write your
  `parseEvent` function (compile-time schema sample). See `examples.md` →
  Example 3.

## Naming Conventions

- `tryParse` returns `option`; `create` is total; `value` unwraps.
- Predicates/assertions are named `assert*` and return `Result<unit, _>`.
- DTO types mirror domain types with a `Dto` suffix; conversion functions are
  `toDto` / `ToDto<...>`.

## Testing Recommendations

- `Alma.Events.TestsUtils` is intentionally part of the public API for use in
  downstream test projects. Reference it from tests, not from production code.
- Normalize volatile fields before asserting on serialized JSON: compose
  `normalizeId`, `normalizeCreatedAt`, `normalizeAllIds` and pass them to
  `assertJsonEquals`. See `examples.md` → Example 10.
- Validate fixtures against their JSON schema with
  `TestsUtils.JsonSchemaValidation.assertJsonSchemaMatch`.
