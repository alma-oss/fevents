namespace Alma.Events

[<RequireQualifiedAccess>]
module CommonParser =
    open System

    let parseDateTime (string: string) =
        DateTimeOffset.Parse(string).DateTime

module Utils =
    open System

    [<RequireQualifiedAccess>]
    module String =
        let (|IsNotEmpty|_|) = function
            | empty when empty |> String.IsNullOrEmpty -> None
            | string -> Some string

    [<AutoOpen>]
    module Regex =
        open System.Text.RegularExpressions

        // http://www.fssnip.net/29/title/Regular-expression-active-pattern
        let (|Regex|_|) pattern input =
            let m = Regex.Match(input, pattern)
            if m.Success then Some (List.tail [ for g in m.Groups -> g.Value ])
            else None
