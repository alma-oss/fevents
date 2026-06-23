# Anti-Patterns

Known pitfalls when using `Alma.Events`, in **mistake → why → fix** form.

## Using `CloudEvent.mapData` for a transform that can fail

- **Why it's wrong**: `mapData` is `mapDataResult (f >> Ok) >> Result.orFail` —
  it throws on the `Error` path instead of returning it. A failing transform
  becomes an unhandled exception.
- **Fix**: use `CloudEvent.mapDataResult` whenever the transform can fail and
  keep the `Result`. Reserve `mapData` for total transforms. See
  `examples.md` → Example 5.

## Feeding `mapData`/`mapDataResult` the wrong source type

- **Why it's wrong**: both match `event.Data` against the expected input type
  and `failwithf "Unexpected data type"` on mismatch. The data type must equal
  the type produced by your previous `parse`/`mapData` step.
- **Fix**: keep the `'EventA` type parameter aligned with what currently sits in
  `event.Data`; transform in the same order the data was produced.

## Constructing `EventSource` / `DataSchema` from a relative or invalid URI

- **Why it's wrong**: `tryParse` only succeeds for an absolute URI and returns
  `None` otherwise; `CloudEvent.parse` then yields `MissingSource` /
  `MissingDataSchema`.
- **Fix**: always pass an absolute URI string and handle the `None` /
  `Result` case explicitly. See `examples.md` → Example 1.

## Hand-writing error strings instead of using `format`

- **Why it's wrong**: each error DU (`ParseError`, `DtoError`,
  `JsonSchemaValidationError`, and CloudEvent's `ParseError<'ParseEventError>`)
  has a dedicated `format` that produces the canonical message. Hand-rolled
  strings drift and lose detail.
- **Fix**: render with `ParseError.format`, `DtoError.format`, or
  `JsonSchema.JsonSchemaValidationError.format`.

## Serializing a processed event without checking metadata

- **Why it's wrong**: events carrying only `OnlyCreatedAt` metadata are missing
  `ProcessedBy`. `Serialize.processedMetaData` / `MetaData.requireProcessedBy`
  return an error for these, and skipping the check emits an incomplete event.
- **Fix**: gate serialization on `MetaData.requireProcessedBy` (or
  `Serialize.processedMetaData`) before building the DTO. See `examples.md` →
  Example 6.

## Calling the schema validators synchronously by ignoring `AsyncResult`

- **Why it's wrong**: `JsonSchema.parseSchema` and `assertJsonSchemaMatch`
  return `AsyncResult`; treating them as plain values skips both the async and
  the error channel.
- **Fix**: compose inside `asyncResult { }` and run with
  `Async.RunSynchronously` only at the boundary. See `examples.md` → Example 9.

## Using `TestsUtils` from production code

- **Why it's wrong**: `Alma.Events.TestsUtils` is shipped for downstream test
  projects (assertions, GUID/timestamp normalizers). It is not a production
  utility and pulls test-oriented behavior (e.g. `failwithf` on bad input) into
  runtime paths.
- **Fix**: reference `TestsUtils` only from test assemblies. See `examples.md`
  → Example 10.

## Comparing serialized JSON without normalizing volatile fields

- **Why it's wrong**: generated `id`, `correlation_id`, `causation_id`, and
  `created_at` differ on every run, so a raw string compare always fails.
- **Fix**: normalize with `normalizeId` / `normalizeAllIds` /
  `normalizeCreatedAt` before `assertJsonEquals`.

## Adding packages with the NuGet CLI

- **Why it's wrong**: this repo uses Paket. `dotnet add package` desyncs
  `paket.dependencies` / `paket.references`.
- **Fix**: use `dotnet paket install` (or edit the paket files) instead.

## Moving or renaming `src/schema/cloudEvent.json`

- **Why it's wrong**: `CloudEvent.parse` uses it as the compile-time
  `JsonProvider` sample and the fsproj copies it to output. Renaming breaks
  compilation and runtime.
- **Fix**: leave the schema sample path intact; update both the type provider
  path and the fsproj `Content` item together if it ever must change.
