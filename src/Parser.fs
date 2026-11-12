namespace Alma.Events

[<RequireQualifiedAccess>]
module CommonParser =
    open System

    let parseDateTime (string: string) =
        DateTimeOffset.Parse(string).DateTime

module Utils =
    open System
    open System.Globalization

    [<RequireQualifiedAccess>]
    module String =
        let (|IsNotEmpty|_|) = function
            | empty when empty |> String.IsNullOrEmpty -> None
            | string -> Some string

    [<RequireQualifiedAccess>]
    module Uri =
        let tryCreate (kind: UriKind) (uri: string): Uri option =
            match Uri.TryCreate(uri, kind) with
            | true, uri -> Some uri
            | _ -> None

    [<RequireQualifiedAccess>]
    module DateTimeOffset =
        /// Parses an RFC 3339 / ISO 8601 timestamp, keeping its offset.
        let tryParse (value: string): DateTimeOffset option =
            match DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) with
            | true, value -> Some value
            | _ -> None

    [<AutoOpen>]
    module Regex =
        open System.Text.RegularExpressions

        // http://www.fssnip.net/29/title/Regular-expression-active-pattern
        let (|Regex|_|) pattern input =
            let m = Regex.Match(input, pattern)
            if m.Success then Some (List.tail [ for g in m.Groups -> g.Value ])
            else None
