module Events.CloudEventEnvelope

open System
open Expecto
open FSharp.Data
open Alma.Events
open Alma.EnvironmentModel
open Alma.Kafka
open Alma.ServiceIdentification
open Alma.Serializer

let private okOrFail message = function
    | Ok value -> value
    | Error error -> failtestf "%s: %A" message error

let private instance = Instance.parseStrict "-" "alma-status-prod-stable" |> okOrFail "fixture instance"
let private environment = Environment.parse "dev1-services" |> okOrFail "fixture environment"
let private recordedTime = ExtensionName.create "recordedtime" |> okOrFail "fixture extension name"

type private IncidentData = {
    Id: string
    Severity: string
}

let private parseIncident (data: string): Result<IncidentData, exn> =
    try
        let json = JsonValue.Parse data
        Ok { Id = json["id"].AsString(); Severity = json["severity"].AsString() }
    with e -> Error e

let private envelope =
    Envelope.create (EventId (Guid "01a16833-fd00-7000-8000-042b88a88410")) (EventName "status.incident.started") (EventSource.ofSoftwareComponent (Untyped (instance, environment)))
    |> Envelope.withSubject "web-client/2026-10-23T09:59:40Z"
    |> Envelope.withTime (DateTimeOffset.Parse "2026-10-23T09:59:40Z")
    |> Envelope.withExtension recordedTime (ExtensionValue.Timestamp (DateTimeOffset.Parse "2026-10-23T10:00:00Z"))

let private data = JsonValue.Parse """{ "id": "web-client/2026-10-23T09:59:40Z", "severity": "critical" }""" |> Serialize.JsonValue.toSerializableJson

[<Tests>]
let eventSourceTests =
    testList "EventSource" [
        testCase "tryParse keeps the strict form" <| fun _ ->
            Expect.isSome (EventSource.tryParse "https://example.test/webapi") "absolute URI"
            Expect.isNone (EventSource.tryParse "alma-status-prod-stable@dev1-services") "relative reference"

        testCase "tryParseReference accepts any non-empty URI-reference" <| fun _ ->
            Expect.isSome (EventSource.tryParseReference "https://example.test/webapi") "absolute URI"
            Expect.isSome (EventSource.tryParseReference "alma-status-prod-stable@dev1-services") "relative reference"
            Expect.isSome (EventSource.tryParseReference "/sensors/tn-1") "path reference"
            Expect.isNone (EventSource.tryParseReference "") "empty"
            Expect.isNone (EventSource.tryParseReference "   ") "blank"

        testCase "ofSoftwareComponent renders instance@environment" <| fun _ ->
            Expect.equal
                (EventSource.ofSoftwareComponent (Untyped (instance, environment)) |> EventSource.value |> string)
                "alma-status-prod-stable@dev1-services"
                "software component"
    ]

[<Tests>]
let extensionNameTests =
    testList "ExtensionName" [
        testCase "accepts lowercase letters and digits" <| fun _ ->
            Expect.equal (ExtensionName.create "recordedtime" |> Result.map ExtensionName.value) (Ok "recordedtime") "letters"
            Expect.equal (ExtensionName.create "trace1" |> Result.map ExtensionName.value) (Ok "trace1") "digits"

        testCase "rejects other characters and the context attributes" <| fun _ ->
            Expect.equal (ExtensionName.create "RecordedTime") (Error (ExtensionNameError.InvalidFormat "RecordedTime")) "uppercase"
            Expect.equal (ExtensionName.create "recorded_time") (Error (ExtensionNameError.InvalidFormat "recorded_time")) "underscore"
            Expect.equal (ExtensionName.create "") (Error (ExtensionNameError.InvalidFormat "")) "empty"
            Expect.equal (ExtensionName.create "specversion") (Error (ExtensionNameError.Reserved "specversion")) "reserved"
            Expect.equal (ExtensionName.create "data") (Error (ExtensionNameError.Reserved "data")) "reserved data"
    ]

[<Tests>]
let envelopeTests =
    testList "CloudEvent.createFromEnvelope" [
        testCase "serializes the spec form: relative source, no dataschema, extension attribute" <| fun _ ->
            let json = CloudEvent.createFromEnvelope envelope data |> CloudEvent.toJson [] |> JsonValue.Parse

            Expect.equal (json["specversion"].AsString()) "1.0" "specversion"
            Expect.equal (json["id"].AsString()) "01a16833-fd00-7000-8000-042b88a88410" "id"
            Expect.equal (json["type"].AsString()) "status.incident.started" "type"
            Expect.equal (json["source"].AsString()) "alma-status-prod-stable@dev1-services" "source"
            Expect.equal (json["subject"].AsString()) "web-client/2026-10-23T09:59:40Z" "subject"
            Expect.equal (json["datacontenttype"].AsString()) "application/json" "content type"
            Expect.isNone (json.TryGetProperty "dataschema") "no dataschema"
            Expect.equal (json["time"].AsDateTimeOffset()) (DateTimeOffset.Parse "2026-10-23T09:59:40Z") "time"
            Expect.equal (json["recordedtime"].AsDateTimeOffset()) (DateTimeOffset.Parse "2026-10-23T10:00:00Z") "extension as RFC 3339"
            let data = json["data"]
            Expect.equal (data["id"].AsString()) "web-client/2026-10-23T09:59:40Z" "data keys as given"
            Expect.equal (data["severity"].AsString()) "critical" "data"

        testCase "parses the spec form back with the extension and the data" <| fun _ ->
            let parsed =
                CloudEvent.createFromEnvelope envelope data
                |> CloudEvent.toJson []
                |> CloudEvent.parse parseIncident
                |> okOrFail "parse"

            Expect.equal (string parsed.Source) "alma-status-prod-stable@dev1-services" "source"
            Expect.equal parsed.Subject "web-client/2026-10-23T09:59:40Z" "subject"
            Expect.isNull parsed.DataSchema "no dataschema"
            Expect.equal (parsed |> CloudEvent.tryExtension recordedTime) (Some (ExtensionValue.String "2026-10-23T10:00:00Z")) "extension carried as string"
            Expect.equal (parsed |> CloudEvent.tryData<IncidentData>) (Some { Id = "web-client/2026-10-23T09:59:40Z"; Severity = "critical" }) "typed data"
            Expect.isNone (parsed |> CloudEvent.tryData<string>) "data of another type"

        testCase "parse still rejects an empty source and an invalid dataschema" <| fun _ ->
            let json (source: string) (dataSchema: string option) =
                sprintf """{ "specversion": "1.0", "id": "01a16833-fd00-7000-8000-042b88a88410", "type": "t", "source": "%s", "time": "2026-10-23T09:59:40Z", "datacontenttype": "application/json"%s, "data": { "id": "a", "severity": "critical" } }"""
                    source
                    (dataSchema |> Option.map (sprintf ""","dataschema": "%s" """) |> Option.defaultValue "")

            match json "" None |> CloudEvent.parse parseIncident with
            | Error (MissingSource _) -> ()
            | other -> failtestf "expected MissingSource, got %A" other

            match json "a@b" (Some "not-a-uri") |> CloudEvent.parse parseIncident with
            | Error (MissingDataSchema _) -> ()
            | other -> failtestf "expected MissingDataSchema, got %A" other

            match json "a@b" (Some "https://example.test/schema.json") |> CloudEvent.parse parseIncident with
            | Ok parsed -> Expect.equal (string parsed.DataSchema) "https://example.test/schema.json" "absolute dataschema kept"
            | Error error -> failtestf "expected Ok, got %A" error
    ]
