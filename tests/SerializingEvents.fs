module Events.SerializingEvents

open Expecto
open System.IO
open System.Text.Json
open Alma.ServiceIdentification

open Alma.Events
open Alma.Events.TestsUtils
open FSharp.Data

let okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

type ProvidedData = {
    Event: string
    Expected: string
    Description: string
}

let event (fileName: string) = $"Fixtures/SerializingEvents/{fileName}" |> File.ReadAllText
let eventJsonValue (event: string) = event |> JsonValue.Parse |> RawJson.JsonValue
let eventJsonElement (event: string) = event |> JsonDocument.Parse |> (fun doc -> doc.RootElement |> RawJson.JsonElement)
let instance (value: string) = Create.Instance(value) |> okOrFail

let provideEvents =
    [
        {
            Description = "ConsentAcquired"
            Event = event "consentAcquired.json"
            Expected = event "consentAcquired.json"
        }
    ]

open AssertSerializedEvent
open Alma.Serializer

type TestCase = {
    Event: RawJson
    Expected: string
    Description: string
}

[<Tests>]
let serializeEventsTest =
    testList "Events - serialize events" [
        yield!
            provideEvents
            |> List.collect (fun ({ Event = event; Expected = expected; Description = description }) ->
                [
                    { Event = eventJsonValue event; Expected = expected; Description = $"{description} - JsonValue" }
                    { Event = eventJsonElement event; Expected = expected; Description = $"{description} - JsonElement" }
                ]
            )
            |> List.map (fun ({ Event = event; Expected = expected; Description = description }) ->
                testCase $"events to json string {description}" <| fun _ ->
                    event
                    |> RawJson.toSerializableJsonIgnoringNullsInRecord
                    |> Serialize.toJsonPretty
                    |> Actual
                    |> assertJsonEquals Expect.equal
                        (normalizeId >> normalizeCreatedAt)
                        description
                        (Expected expected)
                )
    ]
