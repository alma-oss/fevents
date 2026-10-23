namespace Alma.Events

open System
open System.Text
open CloudNative.CloudEvents
open CloudNative.CloudEvents.Core
open CloudNative.CloudEvents.Http
open CloudNative.CloudEvents.NewtonsoftJson

open Alma.Kafka
open Alma.EnvironmentModel
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
    /// Strict form: an absolute URI.
    let tryParse (source: string) =
        match Uri.TryCreate(source, UriKind.Absolute) with
        | true, uri -> Some (EventSource uri)
        | _ -> None

    /// Spec form: any non-empty URI-reference, relative ones included (`instance@environment`, `/sensors/tn-1`).
    let tryParseReference (source: string) =
        if String.IsNullOrWhiteSpace source then None
        else
            match Uri.TryCreate(source, UriKind.RelativeOrAbsolute) with
            | true, uri -> Some (EventSource uri)
            | _ -> None

    /// The software component (`instance@environment`) that emitted the event.
    let ofSoftwareComponent (softwareComponent: SoftwareComponent) =
        EventSource (Uri(softwareComponent |> SoftwareComponent.value, UriKind.Relative))

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
    /// A `dataschema` is present but not an absolute URI (an absent one is allowed).
    | MissingDataSchema of string
    | EventParseError of string * 'ParseEventError

//
// Extension attributes (CloudEvents spec: lowercase letters and digits, the spec's attribute types)
//

type ExtensionName = private ExtensionName of string

[<RequireQualifiedAccess>]
type ExtensionNameError =
    | InvalidFormat of given: string
    | Reserved of given: string

[<RequireQualifiedAccess>]
module ExtensionNameError =
    let format = function
        | ExtensionNameError.InvalidFormat given -> sprintf "Extension attribute name %A must consist of lowercase letters and digits only." given
        | ExtensionNameError.Reserved given -> sprintf "Extension attribute name %A is a context attribute of the CloudEvents spec." given

[<RequireQualifiedAccess>]
module ExtensionName =
    let private reserved = set [ "specversion"; "id"; "source"; "type"; "subject"; "time"; "datacontenttype"; "dataschema"; "data"; "data_base64" ]

    let create (name: string): Result<ExtensionName, ExtensionNameError> =
        if String.IsNullOrEmpty name || not (name |> Seq.forall (fun c -> Char.IsAsciiLetterLower c || Char.IsAsciiDigit c)) then
            Error (ExtensionNameError.InvalidFormat name)
        elif reserved.Contains name then
            Error (ExtensionNameError.Reserved name)
        else
            Ok (ExtensionName name)

    let value (ExtensionName name) = name

[<RequireQualifiedAccess>]
type ExtensionValue =
    | String of string
    | Timestamp of DateTimeOffset
    | Integer of int
    | Boolean of bool

[<RequireQualifiedAccess>]
module ExtensionValue =
    let private attributeType = function
        | ExtensionValue.String _ -> CloudEventAttributeType.String
        | ExtensionValue.Timestamp _ -> CloudEventAttributeType.Timestamp
        | ExtensionValue.Integer _ -> CloudEventAttributeType.Integer
        | ExtensionValue.Boolean _ -> CloudEventAttributeType.Boolean

    let internal attribute (name: ExtensionName) (value: ExtensionValue) =
        CloudEventAttribute.CreateExtension(ExtensionName.value name, attributeType value)

    let internal boxed = function
        | ExtensionValue.String value -> box value
        | ExtensionValue.Timestamp value -> box value
        | ExtensionValue.Integer value -> box value
        | ExtensionValue.Boolean value -> box value

    let internal ofObject (value: obj) =
        match value with
        | null -> None
        | :? string as value -> Some (ExtensionValue.String value)
        | :? DateTimeOffset as value -> Some (ExtensionValue.Timestamp value)
        | :? int as value -> Some (ExtensionValue.Integer value)
        | :? bool as value -> Some (ExtensionValue.Boolean value)
        | other -> Some (ExtensionValue.String (string other))

    let value = function
        | ExtensionValue.String value -> value
        | ExtensionValue.Timestamp value -> value.ToString("o")
        | ExtensionValue.Integer value -> string value
        | ExtensionValue.Boolean value -> if value then "true" else "false"

type Extension = {
    Name: ExtensionName
    Value: ExtensionValue
}

/// Context attributes of an envelope; only `id`, `type` and `source` are required by the spec.
type Envelope = {
    Id: EventId
    Type: EventName
    Source: EventSource
    Subject: string option
    DataSchema: DataSchema option
    Time: DateTimeOffset option
    Extensions: Extension list
}

[<RequireQualifiedAccess>]
module Envelope =
    let create (id: EventId) (eventType: EventName) (source: EventSource): Envelope = {
        Id = id
        Type = eventType
        Source = source
        Subject = None
        DataSchema = None
        Time = None
        Extensions = []
    }

    let withSubject (subject: string) (envelope: Envelope) = { envelope with Subject = Some subject }
    let withDataSchema (dataSchema: DataSchema) (envelope: Envelope) = { envelope with DataSchema = Some dataSchema }
    let withTime (time: DateTimeOffset) (envelope: Envelope) = { envelope with Time = Some time }

    let withExtension (name: ExtensionName) (value: ExtensionValue) (envelope: Envelope) =
        { envelope with Extensions = envelope.Extensions @ [ ({ Name = name; Value = value }: Extension) ] }

[<RequireQualifiedAccess>]
module CloudEvent =
    let private createCloudEvent (specVersion: CloudEventsSpecVersion) (envelope: Envelope) contentType (event: 'Event) =
        let cloudEvent = CloudEvent(specVersion)
        cloudEvent.Id <- envelope.Id |> EventId.value |> string
        cloudEvent.Source <- envelope.Source |> EventSource.value
        cloudEvent.Type <- envelope.Type |> EventName.value
        cloudEvent.DataContentType <- contentType |> ContentType.value

        envelope.DataSchema |> Option.iter (fun dataSchema -> cloudEvent.DataSchema <- dataSchema |> DataSchema.value)
        envelope.Subject |> Option.iter (fun subject -> cloudEvent.Subject <- subject)
        envelope.Time |> Option.iter (fun time -> cloudEvent.Time <- Nullable time)

        for extension in envelope.Extensions do
            cloudEvent[ExtensionValue.attribute extension.Name extension.Value] <- ExtensionValue.boxed extension.Value

        cloudEvent.Data <- event

        Validation.CheckCloudEventArgument(cloudEvent, "cloudEvent")

    /// Spec form: any envelope (relative `source`, no `dataschema`, extension attributes).
    let createFromEnvelope (envelope: Envelope) (event: 'Event) =
        createCloudEvent CloudEventsSpecVersion.V1_0 envelope ApplicationJson event

    /// Strict form: `id` and `time` from the common event, absolute `source` and `dataschema` required.
    let create toCommon subject eventType dataSchema source (event: 'Event) =
        let commonEvent: CommonEvent = event |> toCommon

        let envelope: Envelope = {
            Id = commonEvent.Id
            Type = eventType
            Source = source
            Subject = subject event
            DataSchema = Some dataSchema
            Time = Some (DateTimeOffset.Parse commonEvent.Timestamp)
            Extensions = []
        }

        createCloudEvent CloudEventsSpecVersion.V1_0 envelope ApplicationJson event

    /// The populated extension attribute of that name, if any.
    let tryExtension (name: ExtensionName) (cloudEvent: CloudEvent): ExtensionValue option =
        cloudEvent[ExtensionName.value name] |> ExtensionValue.ofObject

    /// The carried data when it is of the expected type.
    let tryData<'Event> (cloudEvent: CloudEvent): 'Event option =
        match cloudEvent.Data with
        | :? 'Event as data -> Some data
        | _ -> None

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
            |> EventSource.tryParseReference
            |> Result.ofOption (MissingSource rawEvent)

        let! dataSchema =
            match parsed.Dataschema with
            | None -> Ok None
            | Some dataSchema -> dataSchema |> DataSchema.tryParse |> Option.map Some |> Result.ofOption (MissingDataSchema rawEvent)

        let! event =
            parsed.Data.JsonValue.ToString()
            |> parseEvent
            |> Result.mapError (fun err -> EventParseError (rawEvent, err))

        let extensions =
            match parsed.JsonValue with
            | JsonValue.Record properties ->
                properties
                |> Array.toList
                |> List.choose (fun (name, value) ->
                    match ExtensionName.create name, value with
                    | Ok name, JsonValue.String value -> Some ({ Name = name; Value = ExtensionValue.String value }: Extension)
                    | Ok name, JsonValue.Boolean value -> Some ({ Name = name; Value = ExtensionValue.Boolean value }: Extension)
                    | Ok name, JsonValue.Number value when Decimal.Truncate value = value && value >= decimal Int32.MinValue && value <= decimal Int32.MaxValue ->
                        Some ({ Name = name; Value = ExtensionValue.Integer (int value) }: Extension)
                    | Ok name, (JsonValue.Number _ | JsonValue.Float _) -> Some ({ Name = name; Value = ExtensionValue.String (value.ToString()) }: Extension)
                    | _ -> None
                )
            | _ -> []

        let envelope: Envelope = {
            Id = EventId parsed.Id
            Type = EventName parsed.Type
            Source = source
            Subject = parsed.Subject
            DataSchema = dataSchema
            Time = Some (DateTimeOffset.Parse parsed.Time)
            Extensions = extensions
        }

        return createCloudEvent specVersion envelope contentType event
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
