namespace Alma.Events

open System
open Alma.Kafka
open Alma.ErrorHandling
open Utils

type CloudEventSpecVersion =
    | V1_0

    override this.ToString() =
        match this with
        | V1_0 -> "1.0"

type ContentType =
    | ApplicationJson

type EventSource = EventSource of Uri
type DataSchema = DataSchema of Uri

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

/// Generic CloudEvent type following CloudEvents v1.0 specification
type CloudEvent<'Event> = {
    /// The version of the CloudEvents specification which the event uses (required)
    SpecVersion: CloudEventSpecVersion

    /// Identifies the event. Producers MUST ensure that source + id is unique for each distinct event (required)
    Id: EventId

    /// Identifies the context in which an event happened (required)
    Source: EventSource

    /// This attribute contains a value describing the type of event related to the originating occurrence (required)
    Type: EventName

    /// Content type of data value
    DataContentType: ContentType

    /// Identifies the schema that data adheres to
    DataSchema: DataSchema

    /// This identifies the subject of the event in the context of the event producer (optional)
    Subject: string option

    /// Timestamp of when the occurrence happened
    Time: string

    /// The event payload
    Data: 'Event
}

type ParseError<'ParseEventError> =
    | InvalidJson of string * exn
    | UnsupportedVersion of string
    | UnsupportedContentType of string
    | MissingSource of string
    | MissingDataSchema of string
    | EventParseError of string * 'ParseEventError

[<RequireQualifiedAccess>]
module CloudEvent =
    let create toCommon subject dataSchema source event =
        let commonEvent: CommonEvent = event |> toCommon

        {
            SpecVersion = V1_0
            Id = commonEvent.Id
            Source = source
            Type = commonEvent.Event
            DataContentType = ApplicationJson
            DataSchema = dataSchema
            Subject = subject event
            Time = commonEvent.Timestamp
            Data = event
        }

    let mapData f cloudEvent =
        { cloudEvent with Data = f cloudEvent.Data }

    open Alma.ErrorHandling
    open FSharp.Data

    type private CloudEventSchema = JsonProvider<"src/schema/cloudEvent.json", SampleIsList=true>

    let parse parseEvent (rawEvent: string) = result {
        let! parsed =
            try rawEvent |> CloudEventSchema.Parse |> Ok
            with ex -> Error (InvalidJson (rawEvent, ex))

        let! specVersion =
            match parsed.Specversion with
            | "1.0" -> Ok V1_0
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

        return {
            SpecVersion = specVersion
            Id = parsed.Id |> EventId
            Source = source
            Type = parsed.Type |> EventName
            DataContentType = contentType
            DataSchema = dataSchema
            Subject =
                match parsed.Subject with
                | Some (String.IsNotEmpty subject) -> Some subject
                | _ -> None
            Time = parsed.Time
            Data = event
        }
    }

/// CloudEvent DTO with only scalar types for serialization/deserialization
type CloudEventDto<'EventDto> = {
    /// The version of the CloudEvents specification which the event uses (required)
    Specversion: string

    /// Identifies the event. Producers MUST ensure that source + id is unique for each distinct event (required)
    Id: string

    /// Identifies the context in which an event happened (required)
    Source: string

    /// This attribute contains a value describing the type of event related to the originating occurrence (required)
    Type: string

    /// Content type of data value
    Datacontenttype: string

    /// Identifies the schema that data adheres to
    Dataschema: string

    /// This identifies the subject of the event in the context of the event producer (optional)
    Subject: string

    /// Timestamp of when the occurrence happened
    Time: string

    /// The event payload as raw string/JSON
    Data: 'EventDto
}

[<RequireQualifiedAccess>]
module CloudEventDto =
    let fromCloudEvent (dataDto: 'Event -> 'EventDto) (cloudEvent: CloudEvent<'Event>): CloudEventDto<'EventDto> =
        {
            Specversion = cloudEvent.SpecVersion.ToString()
            Id = cloudEvent.Id |> EventId.value |> string
            Source = cloudEvent.Source |> EventSource.value |> string
            Type = cloudEvent.Type |> EventName.value
            Datacontenttype =
                match cloudEvent.DataContentType with
                | ApplicationJson -> "application/json"
            Dataschema = cloudEvent.DataSchema |> DataSchema.value |> string
            Subject =
                match cloudEvent.Subject with
                | Some subject -> subject
                | None -> null
            Time = cloudEvent.Time
            Data = dataDto cloudEvent.Data
        }

    let fromCloudEventResult (dataDto: 'Event -> Result<'EventDto, 'Error>) (cloudEvent: CloudEvent<'Event>): Result<CloudEventDto<'EventDto>, 'Error> =
        result {
            let! data = dataDto cloudEvent.Data

            return fromCloudEvent (fun _ -> data) cloudEvent
        }
