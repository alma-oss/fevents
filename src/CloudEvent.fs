namespace Alma.Events

open System
open System.Text
open CloudNative.CloudEvents
open CloudNative.CloudEvents.Core
open CloudNative.CloudEvents.Http
open CloudNative.CloudEvents.NewtonsoftJson

open Alma.Kafka
open Feather.ErrorHandling
open Alma.Serializer

type ContentType =
    | ApplicationJson

type EventSource = EventSource of Uri
type DataSchema = DataSchema of Uri

[<RequireQualifiedAccess>]
module ContentType =
    let value = function
        | ApplicationJson -> "application/json"

[<RequireQualifiedAccess>]
module EventSource =
    let tryParse (source: string) =
        match Uri.TryCreate(source, UriKind.Absolute) with
        | true, uri -> Some (EventSource uri)
        | _ -> None

    let value (EventSource eventSource) = eventSource

[<RequireQualifiedAccess>]
module DataSchema =
    let tryParse (dataSchema: string) =
        match Uri.TryCreate(dataSchema, UriKind.Absolute) with
        | true, uri -> Some (DataSchema uri)
        | _ -> None

    let value (DataSchema dataSchema) = dataSchema

type ParseError<'ParseEventError> =
    | InvalidJson of string * exn
    | UnsupportedVersion of string
    | UnsupportedContentType of string
    | MissingSource of string
    | MissingDataSchema of string
    | EventParseError of string * 'ParseEventError

[<RequireQualifiedAccess>]
module CloudEvent =
    let private createCloudEvent (specVersion: CloudEventsSpecVersion) id source eventType subject dataSchema timestamp contentType (event: 'Event) =
        let cloudEvent = new CloudEvent(specVersion)
        cloudEvent.Id <- id |> EventId.value |> string
        cloudEvent.Source <- source |> EventSource.value
        cloudEvent.Type <- eventType |> EventName.value
        cloudEvent.DataContentType <- contentType |> ContentType.value
        cloudEvent.DataSchema <- dataSchema |> DataSchema.value

        match subject with
        | Some subj -> cloudEvent.Subject <- subj
        | None -> ()

        cloudEvent.Time <- Nullable(DateTimeOffset.Parse timestamp)
        cloudEvent.Data <- event

        Validation.CheckCloudEventArgument(cloudEvent, "cloudEvent")

    let create toCommon subject eventType dataSchema source (event: 'Event) =
        let commonEvent: CommonEvent = event |> toCommon

        createCloudEvent
            CloudEventsSpecVersion.V1_0
            commonEvent.Id
            source
            eventType
            (subject event)
            dataSchema
            commonEvent.Timestamp
            ApplicationJson
            event

    let mapDataResult (f: 'EventA -> Result<'EventB, 'Error>) (event: CloudEvent): Result<CloudEvent, 'Error> =
        match event.Data with
        | :? 'EventA as data ->
            match data |> f with
            | Ok data ->
                event.Data <- data
                Ok event
            | Error e -> Error e

        | data -> data.GetType() |> failwithf "Unexpected data type: %A"

    let mapData (f: 'EventA -> 'EventB) = mapDataResult (f >> Ok) >> Result.orFail

    open FSharp.Data

    type private CloudEventSchema = JsonProvider<"src/schema/cloudEvent.json", SampleIsList=true>

    let parse parseEvent (rawEvent: string) = result {
        let! parsed =
            try rawEvent |> CloudEventSchema.Parse |> Ok
            with ex -> Error (InvalidJson (rawEvent, ex))

        let! specVersion =
            match parsed.Specversion with
            | "1.0" -> Ok CloudEventsSpecVersion.V1_0
            | _ -> Error (UnsupportedVersion rawEvent)

        let! contentType =
            match parsed.Datacontenttype with
            | "application/json" -> Ok ApplicationJson
            | _ -> Error (UnsupportedContentType rawEvent)

        let! source =
            parsed.Source
            |> EventSource.tryParse
            |> Result.ofOption (MissingSource rawEvent)

        let! dataSchema =
            parsed.Dataschema
            |> DataSchema.tryParse
            |> Result.ofOption (MissingDataSchema rawEvent)

        let! event =
            parsed.Data.JsonValue.ToString()
            |> parseEvent
            |> Result.mapError (fun err -> EventParseError (rawEvent, err))

        return createCloudEvent
            specVersion
            (EventId parsed.Id)
            source
            (EventName parsed.Type)
            parsed.Subject
            dataSchema
            parsed.Time
            contentType
            event
    }

    let private createFormatter options =
        JsonEventFormatter(Serialize.createSerializer options)

    let toHttpContent options (cloudEvent: CloudEvent) =
        cloudEvent.ToHttpContent(ContentMode.Structured, createFormatter options)

    let toJson options (cloudEvent: CloudEvent): string =
        let formatter = createFormatter options
        let bytes: ReadOnlyMemory<byte> =
            cloudEvent
            |> formatter.EncodeStructuredModeMessage
            |> fst

        Encoding.UTF8.GetString(bytes.ToArray(), 0, bytes.Length)
