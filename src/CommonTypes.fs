namespace Alma.Events

open System
open System.Text.Json
open FSharp.Data
open Feather.ErrorHandling
open Alma.Kafka
open Alma.Kafka.MetaData

//
// Event
//

[<RequireQualifiedAccess>]
type ParseError =
    | ParseError of string
    | MissingResource
    | InvalidKeyData of exn
    | InvalidKeyDataMessage of string
    | InvalidDomainData of exn
    | InvalidDomainDataMessage of string
    | InvalidEventType of expectedEvent: EventName * given: EventName
    | MetaDataParseError of MetaDataParseError
    | MetaDataMissingProcessedBy
    | EventDataError of string

type Parse<'Event> = RawEvent -> Result<'Event, ParseError>

[<RequireQualifiedAccess>]
module MetaData =
    let requireProcessedBy = function
        | CreatedAndProcessed metaData -> Ok metaData
        | _ -> Error ParseError.MetaDataMissingProcessedBy

    let processed processedBy =
        MetaDataCreatedAndProcessed (CreatedAt.now(), processedBy)

type Transform<'Event, 'TransformedEvent> = ProcessedBy -> 'Event -> 'TransformedEvent
type TransformAsync<'Event, 'TransformedEvent, 'Error> = ProcessedBy -> 'Event -> AsyncResult<'TransformedEvent, 'Error>

[<RequireQualifiedAccess>]
module Transform =
    type ToPublic<'KeyData, 'MetaData, 'DomainData, 'PublicDomainData> =
        ('DomainData -> 'PublicDomainData)
            -> ProcessedBy
            -> Event<'KeyData, 'MetaData, 'DomainData>
            -> Event<'KeyData, MetaData, 'PublicDomainData>

    let toPublic: ToPublic<'KeyData, 'MetaData, 'DomainData, 'PublicDomainData> =
        fun transformDomainData processedBy event ->
            {
                Schema = event.Schema
                Id = Guid.NewGuid() |> EventId
                CorrelationId = event.CorrelationId
                CausationId = CausationId.fromEventId event.Id
                Timestamp = event.Timestamp
                Event = event.Event
                Domain = event.Domain
                Context = event.Context
                Purpose = event.Purpose
                Version = event.Version
                Zone = event.Zone
                Bucket = event.Bucket
                MetaData = MetaData.forProcessedEvent processedBy
                Resource = event.Resource
                KeyData = event.KeyData
                DomainData = event.DomainData |> transformDomainData
            }

[<RequireQualifiedAccess>]
module EventType =
    let assertSame = function
        | expectedEvent, (EventName eventName) when expectedEvent <> eventName -> Error (ParseError.InvalidEventType ((EventName expectedEvent), (EventName eventName)))
        | _ -> Ok ()

[<RequireQualifiedAccess>]
module ParseError =
    let format = function
        | ParseError.ParseError error -> error
        | ParseError.MissingResource -> "Event has no resource."
        | ParseError.InvalidKeyData error -> sprintf "Event has invalid Key Data. More details: %A" error
        | ParseError.InvalidKeyDataMessage error -> sprintf "Event has invalid Key Data. More details: %A" error
        | ParseError.InvalidDomainData error -> sprintf "Event has invalid Domain Data. More details: %A" error
        | ParseError.InvalidDomainDataMessage error -> sprintf "Event has invalid Domain Data. More details: %A" error
        | ParseError.InvalidEventType (expectedEvent, givenType) -> sprintf "Event is in wrong type. Expected %A but %A given." expectedEvent givenType
        | ParseError.MetaDataParseError error -> MetaDataParseError.format error
        | ParseError.MetaDataMissingProcessedBy -> "Event has no processed by metadata."
        | ParseError.EventDataError error -> error

//
// Dto
//

type DtoError<'KeyData, 'MetaData, 'DomainData> =
    | WrongEventType of expectedEvent: string * Event<'KeyData, 'MetaData, 'DomainData>
    | MissingResourceData of Event<'KeyData, 'MetaData, 'DomainData>
    | MissingProcessedMetaData of Event<'KeyData, 'MetaData, 'DomainData>
    | SpecificEventError of message: string * Event<'KeyData, 'MetaData, 'DomainData>
    | InvalidJson of json: string * exn

[<RequireQualifiedAccess>]
module DtoError =
    let assertEventType expectedType (event: Event<_, _, _>) =
        if event.Event <> EventName expectedType
        then Error (WrongEventType (expectedType, event))
        else Ok ()

    let fromParseError event parseError =
        SpecificEventError (parseError |> ParseError.format, event)

    let format = function
        | WrongEventType (expectedType, event) -> sprintf "Event has wrong event type. Expected %s but %A given." expectedType event.Event
        | MissingResourceData event -> sprintf "Event has no resource\n%A" event
        | MissingProcessedMetaData event -> sprintf "Event has no processed data in meta data\n%A" event
        | SpecificEventError (message, event) -> sprintf "%s\n%A" message event
        | InvalidJson (json, error) -> sprintf "Given string is not valid json: %A\nGiven string:\n%s\n" error json

type SerializeDto = obj -> string
type Serialize<'Event, 'KeyData, 'MetaData, 'DomainData> = SerializeDto -> 'Event -> Result<string, DtoError<'KeyData, 'MetaData, 'DomainData>>
type SerializeEvent<'Event, 'SerializeError> = SerializeDto -> 'Event -> Result<string, 'SerializeError>

[<RequireQualifiedAccess>]
module Serialize =
    let createdAtMetaData: MetaData -> MetaDataDto.OnlyCreatedAt = function
        | OnlyCreatedAt (MetaDataOnlyCreatedAt createdAt)
        | CreatedAndProcessed (MetaDataCreatedAndProcessed (createdAt, _)) -> createdAt |> MetaDataDto.fromCreatedAt

    let processedMetaData event: MetaData -> Result<MetaDataDto.CreatedAtAndProcessedBy, _> = function
        | OnlyCreatedAt _ -> Error (MissingProcessedMetaData event)
        | CreatedAndProcessed (MetaDataCreatedAndProcessed (createdAt, processedData)) -> (createdAt, processedData) |> MetaDataDto.fromProcessed |> Ok

    let resource event = function
        | Some resource -> Ok (resource |> Resource.toDto)
        | _ -> Error (MissingResourceData event)

    /// It will deserialize a json string to a generic object and serialize it again by given function
    let reserializeJson serialize json =
        try serialize (Newtonsoft.Json.JsonConvert.DeserializeObject(json)) |> Ok
        with e -> Error (InvalidJson (json, e))

type ToDto<'Event, 'EventDto, 'EventDtoError> = 'Event -> Result<'EventDto, 'EventDtoError>

//
// Others
//

[<RequireQualifiedAccess>]
type RawJson =
    | JsonValue of JsonValue
    | JsonElement of JsonElement

[<RequireQualifiedAccess>]
module RawJson =
    open Alma.Serializer

    let toString = function
        | RawJson.JsonValue value -> value.ToString()
        | RawJson.JsonElement element -> element.Deserialize().ToString()

    let toSerializableJsonIgnoringNullsInRecord = function
        | RawJson.JsonValue value -> value |> Serialize.JsonValue.toSerializableJsonIgnoringNullsInRecord
        | RawJson.JsonElement element -> element |> Serialize.JsonElement.toSerializableJsonIgnoringNullsInRecord

type Key<'Event> = 'Event -> MessageKey

type BrowserInfo = BrowserInfo of string
type IpAddress = IpAddress of string
type Remark = Remark of string
type Source = Source of string

[<RequireQualifiedAccess>]
module BrowserInfo =
    let create = BrowserInfo
    let value (BrowserInfo browserInfo) = browserInfo

[<RequireQualifiedAccess>]
module IpAddress =
    let create = IpAddress
    let value (IpAddress ipAddress) = ipAddress

[<RequireQualifiedAccess>]
module Remark =
    let create = Remark
    let value (Remark remark) = remark

[<RequireQualifiedAccess>]
module Source =
    let create = Source
    let value (Source source) = source
