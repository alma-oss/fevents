namespace Alma.Events

[<RequireQualifiedAccess>]
module JsonSchema =
    open NJsonSchema
    open Feather.ErrorHandling

    type JsonSchemaValidationError =
        | InvalidJson of Validation.ValidationError list

    [<RequireQualifiedAccess>]
    module JsonSchemaValidationError =
        let format = function
            | InvalidJson errors -> errors |> List.map string

    let parseSchema (schema: string) =
        JsonSchema.FromJsonAsync(schema)
        |> AsyncResult.ofTaskCatch id

    let assertJsonSchemaMatch (schema: JsonSchema) (json: string) =
        match schema.Validate(json) |> Seq.toList with
        | [] -> AsyncResult.ofSuccess ()
        | errors -> AsyncResult.ofError (InvalidJson errors)
