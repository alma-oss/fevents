# Examples

All runnable code for the `Alma.Events` skill lives here. Each example is
self-contained. Examples use neutral placeholders (`SomeEvent`, `WebApi`,
`DemoSystem`) — never a real business domain.

---

## 1. Basic — Wrapping a domain primitive (`EventSource`, `DataSchema`)

```fsharp
open Alma.Events

// tryParse returns an option; both expect an absolute URI string.
let source =
    "https://example.test/webapi"
    |> EventSource.tryParse        // Some (EventSource uri) | None

let schema =
    "https://example.test/schemas/some-event.json"
    |> DataSchema.tryParse

let sourceUri = source |> Option.map EventSource.value      // System.Uri
let contentType = ContentType.value ApplicationJson         // "application/json"
```

---

## 2. Basic — Parsing a raw event into a typed event (`Parse<'Event>`)

```fsharp
open Alma.Events
open Alma.Kafka

// A Parse<'Event> is just: RawEvent -> Result<'Event, ParseError>
type DemoEvent = { Id: EventId; Name: EventName }

let parseDemo: Parse<DemoEvent> =
    fun (raw: RawEvent) ->
        // ... read fields from raw ...
        match EventType.assertSame ("demo_event", raw.Event) with
        | Error e -> Error e
        | Ok () -> Ok { Id = raw.Id; Name = raw.Event }

let described =
    match parseDemo someRawEvent with
    | Ok event -> sprintf "ok: %A" event
    | Error err -> ParseError.format err     // human-readable message
```

---

## 3. Realistic — Parse a CloudEvent from JSON, map its data, serialize

```fsharp
open Alma.Events
open Alma.Kafka
open Alma.Serializer
open FSharp.Data

// Domain event + its DTO.
type SomeEvent =
    { Id: EventId; Event: EventName; Timestamp: string; SubjectId: string }

type SomeEventDto = { Id: string; Timestamp: string; Event: string; SubjectId: string }

type SomeEventSchema = JsonProvider<"""{
    "id": "b206e2f4-1a8a-447b-a773-5bb3255eebd7",
    "timestamp": "date-time",
    "event": "some_event",
    "subject_id": "uuid-as-string"
}""">

// parseEvent: string -> Result<SomeEvent, exn>
let parseSomeEvent (data: string) =
    try
        let p = SomeEventSchema.Parse data
        Ok { Id = EventId p.Id; Event = EventName p.Event
             Timestamp = p.Timestamp; SubjectId = p.SubjectId }
    with e -> Error e

let toDto (e: SomeEvent) : SomeEventDto =
    { Id = e.Id |> EventId.value |> string
      Timestamp = e.Timestamp
      Event = e.Event |> EventName.value
      SubjectId = e.SubjectId }

// parse returns Result<CloudEvent, ParseError<'ParseEventError>>
let roundTrip (rawJson: string) =
    match CloudEvent.parse parseSomeEvent rawJson with
    | Ok cloudEvent ->
        let dtoEvent = cloudEvent |> CloudEvent.mapData toDto
        Ok (dtoEvent |> CloudEvent.toJson [ Serialize.Pretty ])
    | Error err -> Error err
```

---

## 4. Realistic — Building a CloudEvent envelope with `CloudEvent.create`

```fsharp
open Alma.Events
open Alma.Kafka

// toCommon: 'Event -> CommonEvent  (supplies Id + Timestamp)
// subject:  'Event -> string option
let wrap toCommon (event: SomeEvent) =
    let source = "https://example.test/webapi" |> EventSource.tryParse |> Option.get
    let dataSchema =
        "https://example.test/schemas/some-event.json"
        |> DataSchema.tryParse |> Option.get
    let eventType = EventName "some_event"
    let subject (_: SomeEvent) = None

    CloudEvent.create toCommon subject eventType dataSchema source event
```

---

## 5. Integration — Fallible mapping with `mapDataResult`

```fsharp
open Alma.Events
open Feather.ErrorHandling

// Use mapDataResult when the transform itself can fail.
// mapData is the throwing variant (f >> Ok >> Result.orFail) — only use it
// when the transform cannot fail.
let validateAndMap (cloudEvent: CloudEvent) =
    cloudEvent
    |> CloudEvent.mapDataResult (fun (e: SomeEvent) ->
        if e.Timestamp = "" then Error "missing timestamp"
        else Ok (toDto e))          // Result<CloudEvent, string>
```

---

## 6. Integration — Transforming an inbound event to a public event

```fsharp
open Alma.Events
open Alma.Kafka

// Transform.toPublic assigns a NEW EventId, sets CausationId from the source
// event's Id (causation chain), and stamps processed metadata.
let toPublicEvent
    (transformDomainData: 'DomainData -> 'PublicDomainData)
    (processedBy: ProcessedBy)
    (event: Event<'KeyData, 'MetaData, 'DomainData>) =

    event |> Transform.toPublic transformDomainData processedBy
    // result: Event<'KeyData, MetaData, 'PublicDomainData>

// Require processed metadata before serializing a processed event.
let ensureProcessed (metaData: MetaData) =
    metaData |> MetaData.requireProcessedBy   // Result<_, ParseError>
```

---

## 7. Integration — Serializing through a DTO (`SerializeDto`, `DtoError`)

```fsharp
open Alma.Events
open Alma.Kafka

// serializeDto: obj -> string  (provided by the host, e.g. from Alma.Serializer)
let serialize (serializeDto: SerializeDto) expectedType toEventDto (event: Event<_, _, _>) =
    match DtoError.assertEventType expectedType event with
    | Error e -> Error (DtoError.format e)
    | Ok () ->
        event
        |> toEventDto
        |> serializeDto
        |> Ok

// reserializeJson: round-trips a JSON string through the serializer.
let normalizeJson serializeDto json =
    Serialize.reserializeJson serializeDto json   // Result<string, DtoError<...>>
```

---

## 8. Integration — Reading raw embedded JSON (`RawJson`)

```fsharp
open Alma.Events
open System.Text.Json
open FSharp.Data

// RawJson abstracts over FSharp.Data JsonValue and System.Text.Json JsonElement.
let fromJsonValue (s: string) = s |> JsonValue.Parse |> RawJson.JsonValue
let fromJsonElement (s: string) =
    JsonDocument.Parse(s).RootElement |> RawJson.JsonElement

let asString (raw: RawJson) = raw |> RawJson.toString

// Strips nulls from records before serializing (both backings supported).
let asSerializable (raw: RawJson) =
    raw |> RawJson.toSerializableJsonIgnoringNullsInRecord
```

---

## 9. Integration — JSON schema validation (`JsonSchema`)

```fsharp
open Alma.Events
open Feather.ErrorHandling

// Both functions return AsyncResult.
let validate (schemaJson: string) (json: string) =
    asyncResult {
        let! schema = JsonSchema.parseSchema schemaJson
        return! json |> JsonSchema.assertJsonSchemaMatch schema
    }
    |> Async.RunSynchronously
    // Error case: JsonSchema.JsonSchemaValidationError.format err -> string list
```

---

## 10. Test — Asserting serialized output (`TestsUtils`)

```fsharp
open Alma.Events
open Alma.Events.TestsUtils
open Alma.Events.TestsUtils.AssertSerializedEvent
open Expecto

// Normalize volatile fields (GUIDs, timestamps) before comparing JSON.
let assertMatches description (expected: string) (actual: string) =
    actual
    |> Actual
    |> assertJsonEquals Expect.equal
        (normalizeId >> normalizeCreatedAt)   // composable normalizers
        description
        (Expected expected)

// normalizeAllIds also rewrites correlation_id / causation_id.
```

---

## 11. Full workflow — parse → transform → serialize → wrap → emit

```fsharp
open Alma.Events
open Alma.Kafka
open Alma.Serializer
open Feather.ErrorHandling

// End-to-end pipeline for an inbound raw event.
let pipeline
    (serializeDto: SerializeDto)
    (processedBy: ProcessedBy)
    (parseInbound: Parse<Event<'K, 'M, 'D>>)
    (transformDomain: 'D -> 'P)
    (toEventDto: Event<'K, MetaData, 'P> -> obj)
    (toCommon: Event<'K, MetaData, 'P> -> CommonEvent)
    (subject: Event<'K, MetaData, 'P> -> string option)
    (eventType: EventName)
    (dataSchema: DataSchema)
    (source: EventSource)
    (raw: RawEvent) =

    raw
    |> parseInbound                                             // Result<_, ParseError>
    |> Result.map (Transform.toPublic transformDomain processedBy)
    |> Result.map (fun publicEvent ->
        let json = publicEvent |> toEventDto |> serializeDto
        let envelope =
            CloudEvent.create toCommon subject eventType dataSchema source publicEvent
        json, (envelope |> CloudEvent.toJson [ Serialize.Pretty ]))
    |> Result.mapError ParseError.format
```
