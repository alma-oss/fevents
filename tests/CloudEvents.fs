module Events.CloudEvents

open Expecto
open System.IO
open System.Text.Json
open Alma.ServiceIdentification

open Alma.Events
open Alma.Events.TestsUtils
open Feather.ErrorHandling
open FSharp.Data
open Alma.Kafka
open Alma.Serializer

[<AutoOpen>]
module SomeEvent =
    // Mock event type for testing
    type SomeEvent = {
        Id: EventId
        Event: EventName
        Timestamp: string
        KeyData: KeyData
    }

    and KeyData = {
        SubjectId: SubjectId
    }

    and SubjectId = SubjectId of string

    type SomeEventDto = {
        Id: string
        Timestamp: string
        Event: string
        KeyData: KeyDataDto
    }

    and KeyDataDto = {
        SubjectId: string
    }

    type SomeEventSchema = JsonProvider<"""{
        "id": "b206e2f4-1a8a-447b-a773-5bb3255eebd7",
        "timestamp": "date-time",
        "event": "some_event",
        "key_data": {
            "subject_id": "uuid-as-string"
        }
    }""">

    // Mock event parser for testing CloudEvent.parse function
    let parseSomeEvent (data: string): Result<SomeEvent, _> =
        try
            let parsed = SomeEventSchema.Parse(data)

            Ok {
                Id = EventId parsed.Id
                Event = EventName parsed.Event
                Timestamp = parsed.Timestamp
                KeyData = { SubjectId = SubjectId parsed.KeyData.SubjectId }
            }
        with e -> Error e

    let toDto (event: SomeEvent) : SomeEventDto =
        {
            Id = event.Id |> EventId.value |> string
            Timestamp = event.Timestamp
            Event = event.Event |> EventName.value
            KeyData = { SubjectId = match event.KeyData.SubjectId with SubjectId id -> id }
        }

let okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

type CloudEventTestData = {
    Event: string
    Expected: string
    Description: string
}

let cloudEvent (fileName: string) = $"Fixtures/CloudEvents/{fileName}" |> File.ReadAllText

let provideCloudEvents =
    [
        {
            Description = "SomeEvent"
            Event = cloudEvent "someEvent.json"
            Expected = cloudEvent "someEvent.json"
        }
    ]

open AssertSerializedEvent

[<Tests>]
let cloudEventTests =
    testList "CloudEvent parse -> serialize" [
        yield!
            provideCloudEvents
            |> List.map (fun ({ Event = eventJson; Description = description }) ->
                testCase $"parse CloudEvent {description}" <| fun _ ->
                    let result = eventJson |> CloudEvent.parse parseSomeEvent

                    match result with
                    | Ok cloudEvent ->
                        let cloudEventDto = cloudEvent |> CloudEvent.mapData toDto

                        let serialized = cloudEventDto |> CloudEvent.toJson [ Serialize.Pretty ]
                        let fromHttpResponse =
                            cloudEvent
                            |> CloudEvent.toHttpContent [ Serialize.Pretty ]
                            |> fun (content) -> content.ReadAsStringAsync().Result

                        Expect.equal serialized fromHttpResponse "Serialization from HttpContent should match direct serialization"

                        serialized
                        |> Actual
                        |> assertJsonEquals Expect.equal
                            (normalizeId >> normalizeCreatedAt)
                            description
                            (Expected eventJson)

                    | Error err ->
                        failtestf "Failed to parse CloudEvent: %A" err
            )
    ]
