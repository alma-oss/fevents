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
    Envelope.create (EventId (Guid "01a16833-fd00-7000-8000-042b88a88410")) (EventName "status.incident.started") (EventSource.SoftwareComponent (Untyped (instance, environment)))
    |> Envelope.withSubject "web-client/2026-10-23T09:59:40Z"
    |> Envelope.withTime (DateTimeOffset.Parse "2026-10-23T09:59:40Z")
    |> Envelope.withExtension recordedTime (ExtensionValue.Timestamp (DateTimeOffset.Parse "2026-10-23T10:00:00Z"))

let private data = JsonValue.Parse """{ "id": "web-client/2026-10-23T09:59:40Z", "severity": "critical" }""" |> Serialize.JsonValue.toSerializableJson

[<Tests>]
let eventSourceTests =
    testList "EventSource" [
        testCase "tryParse accepts any non-empty URI-reference" <| fun _ ->
            Expect.equal (EventSource.tryParse "https://example.test/webapi") (Some (EventSource.EventSource (Uri "https://example.test/webapi"))) "absolute URI"
            Expect.equal (EventSource.tryParse "alma-status-prod-stable@dev1-services") (Some (EventSource.SoftwareComponent (Untyped (instance, environment)))) "software component"
            Expect.equal (EventSource.tryParse "/sensors/tn-1") (Some (EventSource.Reference (Uri("/sensors/tn-1", UriKind.Relative)))) "path reference"
            Expect.equal (EventSource.tryParse "1-555-123-4567") (Some (EventSource.Reference (Uri("1-555-123-4567", UriKind.Relative)))) "application-specific identifier"
            Expect.isNone (EventSource.tryParse "") "empty"
            Expect.isNone (EventSource.tryParse "   ") "blank"

        testCase "tryParse does not coerce a path into an absolute URI" <| fun _ ->
            Expect.equal (EventSource.tryParse "/sensors/tn-1" |> Option.map (EventSource.value >> string)) (Some "/sensors/tn-1") "round trip"

        testCase "tryParse ignores a type prefix, the type travels in the sourcetype extension" <| fun _ ->
            Expect.equal (EventSource.tryParse "AC:alma-status-prod-stable@dev1-services" |> Option.map (function EventSource.EventSource _ -> "absolute" | _ -> "other")) (Some "absolute") "prefix is a scheme"

            let typed code =
                EventSource.tryParseWithExtensions [ { Name = EventSource.typeExtensionName; Value = ExtensionValue.String code } ] "alma-status-prod-stable@dev1-services"

            Expect.equal (typed "ApplicationComponent") (Some (EventSource.SoftwareComponent (ApplicationComponent (instance, environment)))) "application component"
            Expect.equal (typed "SystemSoftware") (Some (EventSource.SoftwareComponent (SystemSoftware (instance, environment)))) "system software"
            Expect.equal (typed "Storage") (Some (EventSource.SoftwareComponent (Storage (instance, environment)))) "storage"
            Expect.equal (typed "unknown") (Some (EventSource.SoftwareComponent (Untyped (instance, environment)))) "unknown type is untyped"

        testCase "SoftwareComponent renders instance@environment and its type as an extension" <| fun _ ->
            Expect.equal
                (EventSource.SoftwareComponent (Storage (instance, environment)) |> EventSource.value |> string)
                "alma-status-prod-stable@dev1-services"
                "software component"

            Expect.equal
                (EventSource.SoftwareComponent (Storage (instance, environment)) |> EventSource.typeExtension)
                (Some { Name = EventSource.typeExtensionName; Value = ExtensionValue.String "Storage" })
                "storage type"

            Expect.isNone (EventSource.SoftwareComponent (Untyped (instance, environment)) |> EventSource.typeExtension) "untyped has no type"
            Expect.isNone (EventSource.EventSource (Uri "https://example.test/webapi") |> EventSource.typeExtension) "absolute URI has no type"
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

        testCase "serializes and parses a typed software component through the sourcetype extension" <| fun _ ->
            let typedEnvelope = { envelope with Source = EventSource.SoftwareComponent (ApplicationComponent (instance, environment)) }
            let json = CloudEvent.createFromEnvelope typedEnvelope data |> CloudEvent.toJson []

            let serialized = JsonValue.Parse json
            Expect.equal (serialized["sourcetype"].AsString()) "ApplicationComponent" "sourcetype extension"

            let parsed = json |> CloudEvent.parse parseIncident |> okOrFail "parse"

            Expect.equal (string parsed.Source) "alma-status-prod-stable@dev1-services" "source"
            Expect.equal (parsed |> CloudEvent.tryExtension EventSource.typeExtensionName) (Some (ExtensionValue.String "ApplicationComponent")) "sourcetype carried"
            Expect.equal (parsed.Source |> string |> EventSource.tryParseWithExtensions [ { Name = EventSource.typeExtensionName; Value = ExtensionValue.String "ApplicationComponent" } ])
                (Some (EventSource.SoftwareComponent (ApplicationComponent (instance, environment))))
                "typed source"

        testCase "serializes a relative reference source" <| fun _ ->
            let json =
                CloudEvent.createFromEnvelope { envelope with Source = EventSource.Reference (Uri("/sensors/tn-1", UriKind.Relative)) } data
                |> CloudEvent.toJson []

            let serialized = JsonValue.Parse json
            Expect.equal (serialized["source"].AsString()) "/sensors/tn-1" "source"

            let parsed = json |> CloudEvent.parse parseIncident |> okOrFail "parse"
            Expect.equal (string parsed.Source) "/sensors/tn-1" "parsed source"

        testCase "parses an envelope without time back" <| fun _ ->
            let parsed =
                CloudEvent.createFromEnvelope { envelope with Time = None } data
                |> CloudEvent.toJson []
                |> CloudEvent.parse parseIncident
                |> okOrFail "parse"

            Expect.isFalse parsed.Time.HasValue "no time"

        testCase "parse returns an error for an invalid time" <| fun _ ->
            let json = """{ "specversion": "1.0", "id": "01a16833-fd00-7000-8000-042b88a88410", "type": "t", "source": "alma-status-prod-stable@dev1-services", "time": "yesterday", "datacontenttype": "application/json", "data": { "id": "a", "severity": "critical" } }"""

            match json |> CloudEvent.parse parseIncident with
            | Error (InvalidTime _) -> ()
            | other -> failtestf "expected InvalidTime, got %A" other

        testCase "parse still rejects an empty source and an invalid dataschema" <| fun _ ->
            let softwareComponent = "alma-status-prod-stable@dev1-services"

            let json (source: string) (dataSchema: string option) =
                sprintf """{ "specversion": "1.0", "id": "01a16833-fd00-7000-8000-042b88a88410", "type": "t", "source": "%s", "time": "2026-10-23T09:59:40Z", "datacontenttype": "application/json"%s, "data": { "id": "a", "severity": "critical" } }"""
                    source
                    (dataSchema |> Option.map (sprintf ""","dataschema": "%s" """) |> Option.defaultValue "")

            match json "" None |> CloudEvent.parse parseIncident with
            | Error (MissingSource _) -> ()
            | other -> failtestf "expected MissingSource, got %A" other

            match json softwareComponent (Some "not-a-uri") |> CloudEvent.parse parseIncident with
            | Error (MissingDataSchema _) -> ()
            | other -> failtestf "expected MissingDataSchema, got %A" other

            match json softwareComponent (Some "https://example.test/schema.json") |> CloudEvent.parse parseIncident with
            | Ok parsed -> Expect.equal (string parsed.DataSchema) "https://example.test/schema.json" "absolute dataschema kept"
            | Error error -> failtestf "expected Ok, got %A" error
    ]

module Readme =
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

    let parseIncident (data: string): Result<IncidentData, exn> =
        try
            let json = JsonValue.Parse data
            Ok { Id = json["id"].AsString(); Severity = json["severity"].AsString() }
        with e -> Error e

[<Tests>]
let readmeTests =
    testList "README example" [
        testCase "produces the documented JSON" <| fun _ ->
            let expected = """{
                  "specversion": "1.0",
                  "id": "01a16833-fd00-7000-8000-042b88a88410",
                  "source": "alma-status-prod-stable@dev1-services",
                  "type": "status.incident.started",
                  "datacontenttype": "application/json",
                  "subject": "web-client/2026-10-23T09:59:40Z",
                  "time": "2026-10-23T09:59:40Z",
                  "recordedtime": "2026-10-23T10:00:00Z",
                  "data": { "id": "web-client/2026-10-23T09:59:40Z", "severity": "critical" }
                }"""
            Expect.equal (JsonValue.Parse Readme.json) (JsonValue.Parse expected) (sprintf "README json, actual: %s" Readme.json)

        testCase "parses back as documented" <| fun _ ->
            let parsed = Readme.json |> CloudEvent.parse Readme.parseIncident |> okOrFail "parse"

            Expect.equal (parsed |> CloudEvent.tryExtension Readme.recordedTime) (Some (ExtensionValue.String "2026-10-23T10:00:00Z")) "extension"
            Expect.equal (parsed |> CloudEvent.tryData<Readme.IncidentData>) (Some { Readme.IncidentData.Id = "web-client/2026-10-23T09:59:40Z"; Severity = "critical" }) "data"
    ]
