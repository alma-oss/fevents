namespace Alma.Events

/// Note: this utils should be used in tests only
module TestsUtils =
    open Alma.Serializer
    open Alma.Events
    open Feather.ErrorHandling

    type AssertJson =
        | Expected of string
        | Actual of string

    let assertJsonEquals assertEquals normalize description jsonA jsonB =
        let splitLines (string: string) =
            let normalizedJson =
                try Serialize.toJsonPretty(Newtonsoft.Json.JsonConvert.DeserializeObject(string))
                with e -> failwithf "Given string is not valid json: %A\nGiven string:\n%s\n" e string

            normalizedJson.Split("\n")

        let normalize serialized =
            serialized
            |> normalize
            |> splitLines
            |> Seq.map (fun s -> s.Trim())
            |> Seq.toList

        let description, actualLines, expectedLines =
            match (jsonA, jsonB) with
            | Expected expected, Actual actual
            | Actual actual, Expected expected ->
                let normalizedActual = actual |> normalize
                let normalizedExpected = expected |> normalize

                let data =
                    normalizedExpected
                    |> List.mapi (fun i expected ->
                        let actual = (normalizedActual |> List.tryItem i |> Option.defaultValue "")

                        sprintf "Exp: %s\nAct: %s%s\n"
                            expected
                            actual
                            (if expected = actual then "" else "    // not equal")
                    )
                    |> String.concat "\n"

                sprintf "%s.\nActual: %s\n---\n%s\n---\n" description actual data,
                normalizedActual,
                normalizedExpected
            | _ -> failwithf "You have to pass exactly 1 expected and 1 actual json (order does not matter)"

        expectedLines
        |> List.iteri (fun i expected ->
            assertEquals actualLines.[i] expected description
        )

    module AssertSerializedEvent =
        let replace key (pattern: string) (replacement: string) (string: string) =
            let pattern = sprintf "%A:%A" key pattern
            let replacement = sprintf "%A:%A" key replacement
            System.Text.RegularExpressions.Regex.Replace(string, pattern, replacement)

        let replaceValue (value: string) (replacement: string) (string: string) =
            string.Replace(value, replacement)

        /// https://stackoverflow.com/questions/11040707/c-sharp-regex-for-guid
        let guidRegex = @"[{(]?[0-9a-f]{8}[-]?(?:[0-9a-f]{4}[-]?){3}[0-9a-f]{12}[)}]?"

        let timeRegex = @"\d{4}\-\d{2}\-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"

        let normalizeId =
            replace "id" guidRegex "<NORMALIZED-ID>"

        let normalizeIdValue idValue =
            replaceValue idValue "<NORMALIZED-ID>"

        let normalizeIdValueTo idValue newIdValueKey =
            replaceValue idValue $"<{newIdValueKey}>"

        let normalizeCreatedAt =
            replace "created_at" timeRegex "<NORMALIZED-TIME>"

        let normalizeAllIds =
            replace "correlation_id" guidRegex "<NORMALIZED-CORRELATION-ID>"
            >> replace "causation_id" guidRegex "<NORMALIZED-CAUSATION-ID>"

    module JsonSchemaValidation =
        open NJsonSchema

        type AssertJsonSchema =
            | ExpectedSchema of string
            | ActualJson of string

        type ValidationError =
            | InvalidSchema of exn
            | InvalidJson of JsonSchema.JsonSchemaValidationError

        let assertJsonSchemaMatch assertOk description entryA entryB =
            asyncResult {
                let schema, json =
                    match entryA, entryB with
                    | ExpectedSchema schema, ActualJson json
                    | ActualJson json, ExpectedSchema schema -> schema, json
                    | _ -> failwithf "You have to pass exactly 1 Json Schema and 1 Json (order does not matter)"

                let! (schema: JsonSchema) =
                    schema
                    |> JsonSchema.parseSchema
                    |> AsyncResult.mapError InvalidSchema

                return!
                    json
                    |> JsonSchema.assertJsonSchemaMatch schema
                    |> AsyncResult.mapError InvalidJson
            }
            |> Async.RunSynchronously
            |> fun result -> assertOk result description
