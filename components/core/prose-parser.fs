module ProseParser

open System
open Types
open PatternLogic
open ExpressionLogic

/// A refusal from the prose front-end. Every case names the construct and
/// carries the source offset, so a refusal is a value, never an exception.
type ProseError =
    | ExpectedMatch of offset: int
    | EmptyMatch of offset: int
    | EmptyProgram of offset: int
    | ExpectedClause of offset: int
    | ExpectedThen of offset: int
    | ExpectedPeriod of offset: int
    | ExpectedFunctionParameters of offset: int
    | ExpectedFunctionEquals of offset: int
    | DuplicateOtherwise of offset: int
    | ClauseAfterOtherwise of offset: int
    | ExpectedMatchSeparator of offset: int
    | ExpectedClose of offset: int
    | UnexpectedClose of offset: int
    | UnexpectedComma of offset: int
    | UnclosedParenthesis of offset: int
    | UnexpectedClosingParenthesis of offset: int
    | ExpectedExpression of offset: int
    | ExpectedPattern of offset: int
    | ExpectedSortName of offset: int
    | ExpectedEquals of offset: int
    | ExpectedBindingName of offset: int
    | ExpectedIn of offset: int
    | UppercaseName of offset: int * text: string
    | UnclosedText of offset: int
    | UnexpectedEnd of offset: int
    | UnexpectedToken of offset: int * text: string
    | LogicOutsideBook of offset: int
    | OpenOutsideBook of offset: int
    | OpenAfterDefinition of offset: int
    | ExpectedOpenName of offset: int
    | UnexpectedSentence of offset: int
    | EmptyBook of offset: int
    | ShapeOutsideBook of offset: int
    | ExpectedShapeName of offset: int
    | ExpectedShapeFields of offset: int
    | DuplicateShape of offset: int
    | ShapeNameConflict of offset: int
    | ShapeArityMismatch of offset: int
    | GenericOutsideBook of offset: int
    | MethodOutsideBook of offset: int
    | ExpectedGenericName of offset: int
    | ExpectedGenericArity of offset: int
    | InvalidGenericArity of offset: int * text: string
    | DuplicateGeneric of offset: int
    | GenericNameConflict of offset: int
    | ExpectedMethodName of offset: int
    | ExpectedMethodParameters of offset: int
    | ExpectedParameterName of offset: int
    | ExpectedParameterColon of offset: int
    | ExpectedParameterShape of offset: int
    | UnknownGeneric of offset: int * name: string
    | UnknownShape of offset: int * name: string
    | MethodArityMismatch of offset: int * name: string
    | DuplicateMethod of offset: int * name: string
    | DuplicateParameter of offset: int * name: string
    | GenericCallArityMismatch of name: string * expected: int * actual: int

/// The book marker. `Logic` is the ordinary definition; `Cycle` is the
/// recursive form and is checked against the definition's calls.
type BookMarker =
    | Logic
    | Cycle

/// One definition in a `.book` file. The body is the raw body; the package
/// unit root is the lambda when parameters are present.
type BookDefinition =
    {
        Name: string
        Parameters: string list
        Body: Expression
        Marker: BookMarker
        Synthetic: bool
    }

/// One nominal constructor declared by `shape name with fields.`. The field
/// order is the construction and pattern argument order.
type ShapeDeclaration = { Name: string; Fields: string list }

/// One generic function declared by `generic name with arity.`.
type GenericDeclaration = { Name: string; Arity: int }

/// One method parameter: a name and the shape it specializes on.
type MethodParameter = { Name: string; Shape: string }

/// One primary method declared by `method name(p : shape ...) = body.`.
type MethodDeclaration =
    {
        Name: string
        Parameters: MethodParameter list
        Body: Expression
    }

/// A parsed book: the definitions, the declared shapes, generics, methods, and
/// the `Let`-threaded program used to bind them. Synthetic dispatcher and method
/// definitions are marked and are not exported.
type BookDocument =
    {
        Definitions: BookDefinition list
        Shapes: ShapeDeclaration list
        Generics: GenericDeclaration list
        Methods: MethodDeclaration list
        Opens: string list
        Program: Expression
    }

let describeError (error: ProseError) : string =
    let offset, message =
        match error with
        | ExpectedMatch at -> at, "a program starts with match"
        | EmptyMatch at -> at, "a match needs at least one choice"
        | EmptyProgram at -> at, "a program needs at least one sentence"
        | ExpectedClause at -> at, "a match clause starts with when or otherwise"
        | ExpectedThen at -> at, "a when clause needs then"
        | ExpectedPeriod at -> at, "a sentence ends with a full stop"
        | ExpectedFunctionParameters at -> at, "an anonymous function starts with ( after logic"
        | ExpectedFunctionEquals at -> at, "an anonymous function parameter list ends with ="
        | DuplicateOtherwise at -> at, "a match has one otherwise choice"
        | ClauseAfterOtherwise at -> at, "otherwise is the last choice"
        | ExpectedMatchSeparator at -> at, "a match head ends with a full stop or a comma"
        | ExpectedClose at -> at, "a nested match or function needs close"
        | UnexpectedClose at -> at, "a close has no open nested match"
        | UnexpectedComma at -> at, "the comma only delimits multiple-choice clauses"
        | UnclosedParenthesis at -> at, "a parenthesis is not closed"
        | UnexpectedClosingParenthesis at -> at, "a closing parenthesis has no opening"
        | ExpectedExpression at -> at, "an expression is required"
        | ExpectedPattern at -> at, "a pattern is required"
        | ExpectedSortName at -> at, "a typed pattern needs a lowercase sort name"
        | ExpectedEquals at -> at, "a binding needs ="
        | ExpectedBindingName at -> at, "a binding needs a name"
        | ExpectedIn at -> at, "a local binding needs in"
        | UppercaseName(at, text) -> at, "a name is lowercase, not " + text
        | UnclosedText at -> at, "a text literal is not closed"
        | UnexpectedEnd at -> at, "the text ends inside a sentence"
        | UnexpectedToken(at, text) -> at, "unexpected token " + text
        | LogicOutsideBook at -> at, "a logic definition belongs in a .book file; prose casts books instead"
        | OpenOutsideBook at -> at, "an open belongs in a .book file"
        | OpenAfterDefinition at -> at, "opens must appear before definitions"
        | ExpectedOpenName at -> at, "an open needs a lowercase module name"
        | UnexpectedSentence at ->
            at, "a book contains logic definitions and includes only; a sentence belongs in a .prose file"
        | EmptyBook at -> at, "a book needs at least one logic definition"
        | ShapeOutsideBook at -> at, "a shape declaration belongs in a .book file; prose casts books instead"
        | ExpectedShapeName at -> at, "a shape declaration needs a lowercase constructor name"
        | ExpectedShapeFields at -> at, "a shape declares fields with with and at least one name, or ends with a period"
        | DuplicateShape at -> at, "a shape is declared twice"
        | ShapeNameConflict at -> at, "a name is both a shape and a logic definition"
        | ShapeArityMismatch at -> at, "a construction does not match the declared field count"
        | GenericOutsideBook at -> at, "a generic declaration belongs in a .book file"
        | MethodOutsideBook at -> at, "a method declaration belongs in a .book file"
        | ExpectedGenericName at -> at, "a generic declaration needs a lowercase name"
        | ExpectedGenericArity at -> at, "a generic declaration needs an arity after with"
        | InvalidGenericArity(at, text) -> at, "a generic arity is a non-negative integer, not " + text
        | DuplicateGeneric at -> at, "a generic is declared twice"
        | GenericNameConflict at -> at, "a name is both a generic and a logic definition or shape"
        | ExpectedMethodName at -> at, "a method declaration needs a generic name"
        | ExpectedMethodParameters at -> at, "a method declares parameters in parentheses"
        | ExpectedParameterName at -> at, "a method parameter needs a name"
        | ExpectedParameterColon at -> at, "a method parameter needs : and a shape"
        | ExpectedParameterShape at -> at, "a method parameter needs a shape after :"
        | UnknownGeneric(at, name) -> at, "a method names an undeclared generic " + name
        | UnknownShape(at, name) -> at, "a method names an undeclared shape " + name
        | MethodArityMismatch(at, name) -> at, "a method of " + name + " does not match the generic arity"
        | DuplicateMethod(at, name) -> at, "a method of " + name + " repeats a specializer list"
        | DuplicateParameter(at, name) -> at, "a method parameter is repeated: " + name
        | GenericCallArityMismatch(name, expected, actual) ->
            0,
            "a call to "
            + name
            + " expects "
            + string expected
            + " argument(s), got "
            + string actual

    message + " at offset " + string offset

type private Token =
    | Word of text: string
    | Name of text: string
    | Keyword of text: string
    | Number of value: float
    | Text of value: string
    | Atom of name: string
    | Period
    | LeftParenthesis
    | RightParenthesis
    | Comma
    | Colon
    | Equals
    | Pipe
    | ReversePipe
    | End

type private Lexeme = { Token: Token; Offset: int }

type private Cursor =
    {
        Tokens: Lexeme array
        Source: string
        EndOffset: int
        mutable Index: int
        Ranges: ResizeArray<ExpressionPath * SourceRange>
    }

let private keywordTexts =
    [
        "match"
        "when"
        "then"
        "otherwise"
        "close"
        "logic"
        "cycle"
        "open"
        "with"
        "function"
        "shape"
        "let"
        "in"
        "generic"
        "method"
    ]

let private isKeyword (text: string) : bool = List.contains text keywordTexts

let private hasUppercase (text: string) : bool =
    text |> Seq.exists (fun character -> Char.IsUpper character)

let private lexRaw (text: string) : Result<Lexeme array, ProseError> =
    let tokens = ResizeArray<Lexeme>()
    let mutable index = 0
    let mutable failure = None

    let add token offset =
        tokens.Add { Token = token; Offset = offset }

    let readWord (start: int) : int * string =
        let mutable finish = start

        while (finish < text.Length && Char.IsLetterOrDigit text.[finish]) do
            finish <- finish + 1

        finish, text.Substring(start, finish - start)

    let readNumber (start: int) : int * string =
        let mutable finish = if text.[start] = '-' then start + 1 else start

        while finish < text.Length && Char.IsDigit text.[finish] do
            finish <- finish + 1

        if
            finish + 1 < text.Length
            && text.[finish] = '.'
            && Char.IsDigit text.[finish + 1]
        then
            finish <- finish + 1

            while finish < text.Length && Char.IsDigit text.[finish] do
                finish <- finish + 1

        finish, text.Substring(start, finish - start)

    while index < text.Length && failure.IsNone do
        let character = text.[index]

        if Char.IsWhiteSpace character then
            index <- index + 1
        else
            match character with
            | '.' ->
                add Period index
                index <- index + 1
            | '(' ->
                add LeftParenthesis index
                index <- index + 1
            | ')' ->
                add RightParenthesis index
                index <- index + 1
            | ',' ->
                add Comma index
                index <- index + 1
            | '=' ->
                add Equals index
                index <- index + 1
            | '|' when index + 1 < text.Length && text.[index + 1] = '>' ->
                add Pipe index
                index <- index + 2
            | '<' when index + 1 < text.Length && text.[index + 1] = '|' ->
                add ReversePipe index
                index <- index + 2
            | '"' ->
                let start = index
                let mutable closing = index + 1

                while closing < text.Length && text.[closing] <> '"' do
                    closing <- closing + 1

                if closing >= text.Length then
                    failure <- Some(UnclosedText start)
                else
                    add (Text(text.Substring(start + 1, closing - start - 1))) start
                    index <- closing + 1
            | ':' ->
                let start = index
                let next = index + 1

                if next < text.Length && Char.IsLetter text.[next] then
                    let finish, name = readWord next

                    if hasUppercase name then
                        failure <- Some(UppercaseName(start, name))
                    else
                        add (Atom name) start
                        index <- finish
                elif next >= text.Length || Char.IsWhiteSpace text.[next] then
                    add Colon start
                    index <- next
                else
                    failure <- Some(UnexpectedToken(start, ":" + string text.[next]))
            | '_' ->
                let start = index

                if index + 1 < text.Length && Char.IsLetterOrDigit text.[index + 1] then
                    failure <- Some(UnexpectedToken(start, "_" + string text.[index + 1]))
                else
                    add (Word "_") start
                    index <- index + 1
            | character when character = '-' && index + 1 < text.Length && Char.IsDigit text.[index + 1] ->
                let start = index
                let finish, raw = readNumber start

                match
                    Double.TryParse(
                        raw,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, value ->
                    add (Number value) start
                    index <- finish
                | false, _ -> failure <- Some(UnexpectedToken(start, raw))
            | character when Char.IsDigit character ->
                let start = index
                let finish, raw = readNumber start

                match
                    Double.TryParse(
                        raw,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, value ->
                    add (Number value) start
                    index <- finish
                | false, _ -> failure <- Some(UnexpectedToken(start, raw))
            | character when Char.IsLetter character ->
                let start = index
                let finish, word = readWord index
                add (Word word) start
                index <- finish
            | other -> failure <- Some(UnexpectedToken(index, string other))

    match failure with
    | Some error -> Error error
    | None -> Ok(tokens.ToArray())

let private combine (vocabulary: VocabularyEntry list) (tokens: Lexeme array) : Result<Lexeme array, ProseError> =
    let result = ResizeArray<Lexeme>()
    let mutable index = 0
    let mutable failure = None

    let wordAt (position: int) : (string * int) option =
        if position < tokens.Length then
            match tokens.[position].Token with
            | Word text -> Some(text, tokens.[position].Offset)
            | _ -> None
        else
            None

    let wordsAt (start: int) (length: int) : string list option =
        let rec collect position remaining accumulated =
            if remaining = 0 then
                Some(List.rev accumulated)
            else
                match wordAt position with
                | Some(text, _) when not (isKeyword text) && not (hasUppercase text) ->
                    collect (position + 1) (remaining - 1) (text :: accumulated)
                | _ -> None

        collect start length []

    let vocabularyToken (form: string) : string option =
        vocabulary
        |> List.tryFind (fun entry -> entry.IsActive && entry.Form = form)
        |> Option.map (fun entry -> entry.Token)

    let rec longestMatch (start: int) (length: int) : (int * string) option =
        if length < 1 then
            None
        else
            match wordsAt start length with
            | Some words ->
                match vocabularyToken (String.concat " " words) with
                | Some token -> Some(length, token)
                | None -> longestMatch start (length - 1)
            | None -> longestMatch start (length - 1)

    let ofPhrase (start: int) : (int * string) option =
        match wordAt start with
        | Some(first, _) when first <> "of" && not (isKeyword first) && not (hasUppercase first) ->
            let rec collect (position: int) (accumulated: string list) (sawOf: bool) : (int * string) option =
                match wordAt position with
                | Some("of", _) ->
                    match wordAt (position + 1) with
                    | Some(next, _) when next <> "of" && not (isKeyword next) && not (hasUppercase next) ->
                        collect (position + 2) (accumulated @ [ "of"; next ]) true
                    | _ -> None
                | _ ->
                    if sawOf then
                        Some(List.length accumulated, String.concat " " accumulated)
                    else
                        None

            collect (start + 1) [ first ] false
        | _ -> None

    while index < tokens.Length && failure.IsNone do
        match tokens.[index].Token with
        | Word text ->
            let offset = tokens.[index].Offset

            if hasUppercase text then
                failure <- Some(UppercaseName(offset, text))
            elif isKeyword text then
                result.Add
                    {
                        Token = Keyword text
                        Offset = offset
                    }

                index <- index + 1
            else
                match longestMatch index (min 3 (tokens.Length - index)) with
                | Some(length, canonical) ->
                    result.Add
                        {
                            Token = Name canonical
                            Offset = offset
                        }

                    index <- index + length
                | None ->
                    (match ofPhrase index with
                     | Some(length, phrase) ->
                         result.Add { Token = Name phrase; Offset = offset }
                         index <- index + length
                     | None ->
                         result.Add { Token = Name text; Offset = offset }
                         index <- index + 1)
        | token ->
            result.Add
                {
                    Token = token
                    Offset = tokens.[index].Offset
                }

            index <- index + 1

    match failure with
    | Some error -> Error error
    | None -> Ok(result.ToArray())

let private peek (cursor: Cursor) : Lexeme =
    if cursor.Index < cursor.Tokens.Length then
        cursor.Tokens.[cursor.Index]
    else
        {
            Token = End
            Offset = cursor.EndOffset
        }

let private advance (cursor: Cursor) : unit = cursor.Index <- cursor.Index + 1
let private tokenAt (cursor: Cursor) : Token = (peek cursor).Token

let private nextTokenIs (expected: Token) (cursor: Cursor) : bool =
    cursor.Index + 1 < cursor.Tokens.Length
    && cursor.Tokens.[cursor.Index + 1].Token = expected
let private offsetAt (cursor: Cursor) : int = (peek cursor).Offset

let private nextToken (cursor: Cursor) : Token =
    if cursor.Index + 1 < cursor.Tokens.Length then
        cursor.Tokens.[cursor.Index + 1].Token
    else
        End

/// A sentence-level command starts with a shell verb followed by a program
/// expression, not by a call parenthesis or a full stop.
let private startsCommand (cursor: Cursor) : bool =
    match tokenAt cursor with
    | Name "run"
    | Name "capture"
    | Name "feed"
    | Name "save" ->
        match nextToken cursor with
        | LeftParenthesis
        | Period
        | End -> false
        | _ -> true
    | _ -> false

let private recordRange (cursor: Cursor) (path: ExpressionPath) (startOffset: int) (endOffset: int) : unit =
    cursor.Ranges.Add(
        path,
        {
            StartOffset = startOffset
            EndOffset = endOffset
        }
    )

let private trimmedEnd (cursor: Cursor) (startOffset: int) (endOffset: int) : int =
    if endOffset <= startOffset || endOffset > cursor.Source.Length then
        endOffset
    else
        let slice = cursor.Source.Substring(startOffset, endOffset - startOffset)
        startOffset + slice.TrimEnd().Length

let rec private parseExpression (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    match parsePipeline cursor path with
    | Error error -> Error error
    | Ok left -> parseReverseRest cursor path left

and private parseReverseRest
    (cursor: Cursor)
    (path: ExpressionPath)
    (left: Expression)
    : Result<Expression, ProseError> =
    match tokenAt cursor with
    | ReversePipe ->
        let startOffset = offsetAt cursor
        advance cursor

        match parseExpression cursor (path @ [ 1 ]) with
        | Error error -> Error error
        | Ok right ->
            recordRange cursor path startOffset (offsetAt cursor)
            Ok(ReversePipeline(left, right))
    | _ -> Ok left

and private parsePipeline (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    match parseStage cursor path with
    | Error error -> Error error
    | Ok left -> parsePipelineRest cursor path left

and private parsePipelineRest
    (cursor: Cursor)
    (path: ExpressionPath)
    (left: Expression)
    : Result<Expression, ProseError> =
    match tokenAt cursor with
    | Pipe ->
        let startOffset = offsetAt cursor
        advance cursor

        match parseStageApplication cursor (path @ [ 1 ]) with
        | Error error -> Error error
        | Ok right ->
            recordRange cursor path startOffset (offsetAt cursor)
            parsePipelineRest cursor path (Pipeline(left, right, LastArgument))
    | _ -> Ok left

and private parseStageApplication (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    match parseStage cursor path with
    | Error error -> Error error
    | Ok target -> parseStageArguments cursor path target

and private parseStageArguments
    (cursor: Cursor)
    (path: ExpressionPath)
    (target: Expression)
    : Result<Expression, ProseError> =
    match tokenAt cursor with
    | Number _
    | Text _
    | Atom _
    | Name _
    | Keyword "match"
    | Keyword "logic" ->
        match parseStage cursor (path @ [ 1 ]) with
        | Error error -> Error error
        | Ok argument ->
            let applied =
                match target with
                | ExpressionLogic.Call(callee, arguments) -> ExpressionLogic.Call(callee, arguments @ [ argument ])
                | other -> ExpressionLogic.Call(other, [ argument ])

            parseStageArguments cursor path applied
    | _ -> Ok target

and private parseStage (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    let token = tokenAt cursor
    let offset = offsetAt cursor

    match token with
    | Number value ->
        advance cursor
        recordRange cursor path offset (trimmedEnd cursor offset (offsetAt cursor))
        Ok(ExpressionLogic.Literal(ExpressionLogic.NumberLiteral value))
    | Text value ->
        advance cursor
        recordRange cursor path offset (trimmedEnd cursor offset (offsetAt cursor))
        Ok(ExpressionLogic.Literal(ExpressionLogic.TextLiteral value))
    | Atom name ->
        advance cursor
        recordRange cursor path offset (trimmedEnd cursor offset (offsetAt cursor))
        Ok(ExpressionLogic.Literal(ExpressionLogic.AtomLiteral name))
    | Keyword "match" -> parseMatch true cursor path
    | Keyword "logic" -> parseAnonymousLogic cursor path
    | Keyword "let" -> parseLet cursor path
    | Keyword _ -> Error(ExpectedExpression offset)
    | Word "_"
    | Name "_" ->
        advance cursor
        recordRange cursor path offset (offsetAt cursor)
        Ok ExpressionLogic.Hole
    | Name name ->
        advance cursor

        match tokenAt cursor with
        | LeftParenthesis ->
            advance cursor
            recordRange cursor (path @ [ 0 ]) offset (offset + name.Length)

            parseArguments cursor path 1 offset []
            |> Result.map (fun arguments ->
                recordRange cursor path offset (offsetAt cursor)

                if arguments |> List.exists (fun argument -> argument = ExpressionLogic.Hole) then
                    ExpressionLogic.Partial(ExpressionLogic.Word name, arguments)
                else
                    ExpressionLogic.Call(ExpressionLogic.Word name, arguments))
        | _ ->
            recordRange cursor path offset (offset + name.Length)
            Ok(ExpressionLogic.Word name)
    | Comma -> Error(UnexpectedComma offset)
    | End -> Error(UnexpectedEnd offset)
    | _ -> Error(ExpectedExpression offset)

and private parseAnonymousLogic (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    let startOffset = offsetAt cursor
    advance cursor

    match tokenAt cursor with
    | LeftParenthesis ->
        advance cursor

        let rec parameters (accumulated: string list) : Result<Expression, ProseError> =
            match tokenAt cursor with
            | Name name ->
                advance cursor
                parameters (name :: accumulated)
            | RightParenthesis ->
                advance cursor

                match tokenAt cursor with
                | Equals ->
                    advance cursor

                    match parseExpression cursor (path @ [ 1 ]) with
                    | Error error -> Error error
                    | Ok body ->
                        match tokenAt cursor with
                        | Keyword "close" ->
                            advance cursor
                            recordRange cursor path startOffset (offsetAt cursor)
                            Ok(ExpressionLogic.Lambda(List.rev accumulated, body))
                        | Period ->
                            recordRange cursor path startOffset (offsetAt cursor)
                            Ok(ExpressionLogic.Lambda(List.rev accumulated, body))
                        | _ -> Error(ExpectedClose(offsetAt cursor))
                | _ -> Error(ExpectedFunctionEquals(offsetAt cursor))
            | _ -> Error(ExpectedBindingName(offsetAt cursor))

        parameters []
    | _ -> Error(ExpectedFunctionParameters(offsetAt cursor))

and private parseLet (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    let startOffset = offsetAt cursor
    advance cursor

    match tokenAt cursor with
    | Name name ->
        advance cursor

        match tokenAt cursor with
        | Equals ->
            advance cursor

            match parseExpression cursor (path @ [ 1 ]) with
            | Error error -> Error error
            | Ok value ->
                match tokenAt cursor with
                | Keyword "in" ->
                    advance cursor

                    match parseExpression cursor (path @ [ 2 ]) with
                    | Error error -> Error error
                    | Ok body ->
                        recordRange cursor path startOffset (offsetAt cursor)
                        Ok(ExpressionLogic.Let(name, value, body))
                | _ -> Error(ExpectedIn(offsetAt cursor))
        | _ -> Error(ExpectedEquals(offsetAt cursor))
    | _ -> Error(ExpectedBindingName(offsetAt cursor))

and private parseArguments
    (cursor: Cursor)
    (path: ExpressionPath)
    (index: int)
    (openOffset: int)
    (accumulated: Expression list)
    : Result<Expression list, ProseError> =
    match tokenAt cursor with
    | RightParenthesis ->
        advance cursor
        Ok(List.rev accumulated)
    | Comma -> Error(UnexpectedComma(offsetAt cursor))
    | End -> Error(UnclosedParenthesis openOffset)
    | _ ->
        match parseExpression cursor (path @ [ index ]) with
        | Error error -> Error error
        | Ok argument -> parseArguments cursor path (index + 1) openOffset (argument :: accumulated)

/// A command sentence: `run "echo" with "hello".` or
/// `capture "echo" with "hello".`. The head is already at the cursor. Each
/// `with` adds one argument; the result is the same call the parenthesized
/// spelling produces.
and private parseCommand (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    let head = peek cursor
    let startOffset = head.Offset

    let name =
        match head.Token with
        | Name value -> value
        | _ -> ""

    advance cursor

    let commandArgument (argumentPath: ExpressionPath) : Result<Expression, ProseError> =
        match tokenAt cursor with
        | Name value ->
            advance cursor
            Ok(ExpressionLogic.Literal(ExpressionLogic.TextLiteral value))
        | Text value ->
            advance cursor
            Ok(ExpressionLogic.Literal(ExpressionLogic.TextLiteral value))
        | _ -> parseExpression cursor argumentPath

    match commandArgument (path @ [ 1 ]) with
    | Error error -> Error error
    | Ok program ->
        let rec arguments (index: int) (accumulated: Expression list) : Result<Expression list, ProseError> =
            match tokenAt cursor with
            | Keyword "with" ->
                advance cursor

                match commandArgument (path @ [ 2 + index ]) with
                | Error error -> Error error
                | Ok argument -> arguments (index + 1) (argument :: accumulated)
            | _ -> Ok(List.rev accumulated)

        match arguments 0 [] with
        | Error error -> Error error
        | Ok values ->
            recordRange cursor path startOffset (offsetAt cursor)
            Ok(ExpressionLogic.Call(ExpressionLogic.Word name, program :: values))

and private parsePattern (cursor: Cursor) : Result<Pattern, ProseError> =
    let token = tokenAt cursor
    let offset = offsetAt cursor

    match token with
    | Name "_" ->
        advance cursor
        Ok PatternLogic.Wildcard
    | Name name ->
        advance cursor

        match tokenAt cursor with
        | LeftParenthesis ->
            advance cursor

            parsePatternChildren cursor offset []
            |> Result.map (fun children -> PatternLogic.Constructor(name, children))
        | _ ->
            let variable = PatternLogic.Variable(name, PatternLogic.Fresh)

            match tokenAt cursor with
            | Colon ->
                advance cursor

                match tokenAt cursor with
                | Name sortName ->
                    advance cursor
                    Ok(PatternLogic.TypedPattern(sortName, variable))
                | _ -> Error(ExpectedSortName(offsetAt cursor))
            | _ -> Ok variable
    | Number value ->
        advance cursor
        Ok(PatternLogic.NumberLiteral value)
    | Text value ->
        advance cursor
        Ok(PatternLogic.TextLiteral value)
    | Atom name ->
        advance cursor
        Ok(PatternLogic.AtomLiteral name)
    | Comma -> Error(UnexpectedComma offset)
    | End -> Error(UnexpectedEnd offset)
    | _ -> Error(ExpectedPattern offset)

and private parsePatternChildren
    (cursor: Cursor)
    (openOffset: int)
    (accumulated: Pattern list)
    : Result<Pattern list, ProseError> =
    match tokenAt cursor with
    | RightParenthesis ->
        advance cursor
        Ok(List.rev accumulated)
    | Comma -> Error(UnexpectedComma(offsetAt cursor))
    | End -> Error(UnclosedParenthesis openOffset)
    | _ ->
        match parsePattern cursor with
        | Error error -> Error error
        | Ok child -> parsePatternChildren cursor openOffset (child :: accumulated)

and private parseMatch (nested: bool) (cursor: Cursor) (path: ExpressionPath) : Result<Expression, ProseError> =
    let head = peek cursor

    match head.Token with
    | Keyword "match" ->
        advance cursor

        match parseExpression cursor path with
        | Error error -> Error error
        | Ok subject ->
            match tokenAt cursor with
            | Period ->
                advance cursor

                let rec sentenceClauses
                    (accumulated: ExpressionClause list)
                    (sawOtherwise: bool)
                    : Result<Expression, ProseError> =
                    let token = tokenAt cursor
                    let offset = offsetAt cursor

                    match token with
                    | End ->
                        if nested then
                            Error(ExpectedClose offset)
                        elif List.isEmpty accumulated then
                            Error(EmptyMatch head.Offset)
                        else
                            Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))
                    | Keyword "when" ->
                        if sawOtherwise then
                            Error(ClauseAfterOtherwise offset)
                        else
                            advance cursor

                            match parsePattern cursor with
                            | Error error -> Error error
                            | Ok pattern ->
                                match tokenAt cursor with
                                | Keyword "then" ->
                                    advance cursor

                                    match parseExpression cursor path with
                                    | Error error -> Error error
                                    | Ok body ->
                                        match tokenAt cursor with
                                        | Period ->
                                            advance cursor

                                            let clause =
                                                {
                                                    Pattern = pattern
                                                    Guard = None
                                                    Body = [ body ]
                                                }

                                            sentenceClauses (clause :: accumulated) sawOtherwise
                                        | Comma -> Error(UnexpectedComma(offsetAt cursor))
                                        | _ -> Error(ExpectedPeriod(offsetAt cursor))
                                | _ -> Error(ExpectedThen(offsetAt cursor))
                    | Keyword "otherwise" ->
                        if sawOtherwise then
                            Error(DuplicateOtherwise offset)
                        else
                            advance cursor

                            match parseExpression cursor path with
                            | Error error -> Error error
                            | Ok body ->
                                match tokenAt cursor with
                                | Period ->
                                    advance cursor

                                    let clause =
                                        {
                                            Pattern = PatternLogic.Wildcard
                                            Guard = None
                                            Body = [ body ]
                                        }

                                    sentenceClauses (clause :: accumulated) true
                                | Comma -> Error(UnexpectedComma(offsetAt cursor))
                                | _ -> Error(ExpectedPeriod(offsetAt cursor))
                    | Keyword "close" ->
                        if nested then
                            if List.isEmpty accumulated then
                                Error(EmptyMatch head.Offset)
                            else
                                advance cursor
                                Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))
                        else
                            Error(UnexpectedClose offset)
                    | Keyword "match" ->
                        if nested then
                            Error(ExpectedClose offset)
                        elif List.isEmpty accumulated then
                            Error(EmptyMatch head.Offset)
                        else
                            Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))
                    | Comma -> Error(UnexpectedComma offset)
                    | _ ->
                        if nested then
                            Error(ExpectedClose offset)
                        elif List.isEmpty accumulated then
                            Error(EmptyMatch head.Offset)
                        else
                            Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))

                sentenceClauses [] false
            | Comma ->
                advance cursor

                let rec inlineChoices
                    (accumulated: ExpressionClause list)
                    (sawOtherwise: bool)
                    : Result<Expression, ProseError> =
                    let token = tokenAt cursor
                    let offset = offsetAt cursor

                    match token with
                    | End ->
                        if nested then
                            Error(ExpectedClose offset)
                        else
                            Error(EmptyMatch head.Offset)
                    | Period ->
                        if nested then
                            Error(ExpectedClose offset)
                        elif List.isEmpty accumulated then
                            Error(EmptyMatch head.Offset)
                        else
                            advance cursor
                            Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))
                    | Keyword "close" ->
                        if nested then
                            if List.isEmpty accumulated then
                                Error(EmptyMatch head.Offset)
                            else
                                advance cursor
                                Ok(ExpressionLogic.PatternMatch(subject, List.rev accumulated))
                        else
                            Error(UnexpectedClose offset)
                    | Keyword "when" ->
                        if sawOtherwise then
                            Error(ClauseAfterOtherwise offset)
                        else
                            advance cursor

                            match parsePattern cursor with
                            | Error error -> Error error
                            | Ok pattern ->
                                match tokenAt cursor with
                                | Keyword "then" ->
                                    advance cursor

                                    match parseExpression cursor path with
                                    | Error error -> Error error
                                    | Ok body ->
                                        let clause =
                                            {
                                                Pattern = pattern
                                                Guard = None
                                                Body = [ body ]
                                            }

                                        match tokenAt cursor with
                                        | Comma ->
                                            let commaOffset = offsetAt cursor
                                            advance cursor

                                            match tokenAt cursor with
                                            | End -> Error(UnexpectedComma commaOffset)
                                            | Period -> Error(UnexpectedComma commaOffset)
                                            | Keyword "close" -> Error(UnexpectedComma commaOffset)
                                            | _ -> inlineChoices (clause :: accumulated) sawOtherwise
                                        | Period ->
                                            if nested then
                                                Error(ExpectedClose(offsetAt cursor))
                                            else
                                                advance cursor

                                                Ok(
                                                    ExpressionLogic.PatternMatch(
                                                        subject,
                                                        List.rev (clause :: accumulated)
                                                    )
                                                )
                                        | Keyword "close" when nested ->
                                            advance cursor
                                            Ok(ExpressionLogic.PatternMatch(subject, List.rev (clause :: accumulated)))
                                        | Keyword "close" -> Error(UnexpectedClose(offsetAt cursor))
                                        | _ -> Error(ExpectedPeriod(offsetAt cursor))
                                | _ -> Error(ExpectedThen(offsetAt cursor))
                    | Keyword "otherwise" ->
                        if sawOtherwise then
                            Error(DuplicateOtherwise offset)
                        else
                            advance cursor

                            match parseExpression cursor path with
                            | Error error -> Error error
                            | Ok body ->
                                let clause =
                                    {
                                        Pattern = PatternLogic.Wildcard
                                        Guard = None
                                        Body = [ body ]
                                    }

                                match tokenAt cursor with
                                | Period ->
                                    if nested then
                                        Error(ExpectedClose(offsetAt cursor))
                                    else
                                        advance cursor
                                        Ok(ExpressionLogic.PatternMatch(subject, List.rev (clause :: accumulated)))
                                | Keyword "close" when nested ->
                                    advance cursor
                                    Ok(ExpressionLogic.PatternMatch(subject, List.rev (clause :: accumulated)))
                                | Keyword "close" -> Error(UnexpectedClose(offsetAt cursor))
                                | Comma ->
                                    let commaOffset = offsetAt cursor
                                    advance cursor

                                    match tokenAt cursor with
                                    | Keyword "when"
                                    | Keyword "otherwise" -> Error(ClauseAfterOtherwise(offsetAt cursor))
                                    | _ -> Error(UnexpectedComma commaOffset)
                                | _ -> Error(ExpectedPeriod(offsetAt cursor))
                    | Keyword "match" -> Error(ExpectedClause offset)
                    | Comma -> Error(UnexpectedComma offset)
                    | _ -> Error(ExpectedClause offset)

                inlineChoices [] false
            | _ -> Error(ExpectedMatchSeparator(offsetAt cursor))
    | _ -> Error(ExpectedMatch head.Offset)

/// The names a pattern binds, for construction rewriting under lexical scope.
let private patternBindings (pattern: Pattern) : Set<string> =
    let rec walk (pattern: Pattern) : Set<string> =
        match pattern with
        | PatternLogic.Wildcard
        | PatternLogic.NumberLiteral _
        | PatternLogic.TextLiteral _
        | PatternLogic.AtomLiteral _ -> Set.empty
        | PatternLogic.Variable(name, _) -> Set.singleton name
        | PatternLogic.Constructor(_, children) ->
            children
            |> List.fold (fun names child -> Set.union names (walk child)) Set.empty
        | PatternLogic.TypedPattern(_, child) -> walk child

    walk pattern

/// Rewrites a call to a declared shape into a `Message` node. Shapes are
/// book-wide, so this runs after the whole book is parsed, and it respects
/// lexical bindings that shadow a shape name.
let rec private rewriteConstructors
    (shapes: Map<string, int>)
    (bound: Set<string>)
    (expression: Expression)
    : Result<Expression, ProseError> =
    let rec rewriteAll (scope: Set<string>) (children: Expression list) : Result<Expression list, ProseError> =
        match children with
        | [] -> Ok []
        | child :: rest ->
            match rewriteConstructors shapes scope child with
            | Error error -> Error error
            | Ok rewritten ->
                rewriteAll scope rest
                |> Result.map (fun rewrittenRest -> rewritten :: rewrittenRest)

    match expression with
    | Literal _
    | ExpressionLogic.Word _
    | Hole -> Ok expression
    | Call(ExpressionLogic.Word name, arguments) when shapes.ContainsKey name && not (bound.Contains name) ->
        if arguments.Length <> shapes.[name] then
            Error(ShapeArityMismatch 0)
        else
            rewriteAll bound arguments
            |> Result.map (fun fields -> ExpressionLogic.Message(name, fields))
    | Call(target, arguments) ->
        rewriteConstructors shapes bound target
        |> Result.bind (fun rewritten ->
            rewriteAll bound arguments
            |> Result.map (fun rewrittenArguments -> ExpressionLogic.Call(rewritten, rewrittenArguments)))
    | Partial(target, arguments) ->
        rewriteConstructors shapes bound target
        |> Result.bind (fun rewritten ->
            rewriteAll bound arguments
            |> Result.map (fun rewrittenArguments -> ExpressionLogic.Partial(rewritten, rewrittenArguments)))
    | Pipeline(value, stage, style) ->
        rewriteConstructors shapes bound value
        |> Result.bind (fun rewrittenValue ->
            rewriteConstructors shapes bound stage
            |> Result.map (fun rewrittenStage -> ExpressionLogic.Pipeline(rewrittenValue, rewrittenStage, style)))
    | ReversePipeline(target, value) ->
        rewriteConstructors shapes bound target
        |> Result.bind (fun rewrittenTarget ->
            rewriteConstructors shapes bound value
            |> Result.map (fun rewrittenValue -> ExpressionLogic.ReversePipeline(rewrittenTarget, rewrittenValue)))
    | Quotation body -> rewriteAll bound body |> Result.map ExpressionLogic.Quotation
    | Block body -> rewriteAll bound body |> Result.map ExpressionLogic.Block
    | Let(name, value, body) ->
        rewriteConstructors shapes bound value
        |> Result.bind (fun rewrittenValue ->
            rewriteConstructors shapes (bound.Add name) body
            |> Result.map (fun rewrittenBody -> ExpressionLogic.Let(name, rewrittenValue, rewrittenBody)))
    | Lambda(parameters, body) ->
        let nextBound =
            List.fold (fun (current: Set<string>) name -> current.Add name) bound parameters

        rewriteConstructors shapes nextBound body
        |> Result.map (fun rewritten -> ExpressionLogic.Lambda(parameters, rewritten))
    | If(condition, thenBranch, elseBranch) ->
        rewriteConstructors shapes bound condition
        |> Result.bind (fun rewrittenCondition ->
            rewriteConstructors shapes bound thenBranch
            |> Result.bind (fun rewrittenThen ->
                rewriteConstructors shapes bound elseBranch
                |> Result.map (fun rewrittenElse ->
                    ExpressionLogic.If(rewrittenCondition, rewrittenThen, rewrittenElse))))
    | PatternMatch(subject, clauses) ->
        rewriteConstructors shapes bound subject
        |> Result.bind (fun rewrittenSubject ->
            let rec rewriteClauses (remaining: ExpressionClause list) (accumulated: ExpressionClause list) =
                match remaining with
                | [] -> Ok(List.rev accumulated)
                | clause :: rest ->
                    let nextBound =
                        patternBindings clause.Pattern
                        |> Set.fold (fun (current: Set<string>) name -> current.Add name) bound

                    rewriteAll nextBound clause.Body
                    |> Result.bind (fun rewrittenBody ->
                        rewriteClauses rest ({ clause with Body = rewrittenBody } :: accumulated))

            rewriteClauses clauses []
            |> Result.map (fun rewrittenClauses -> ExpressionLogic.PatternMatch(rewrittenSubject, rewrittenClauses)))
    | DeclareKind _
    | DeclarePredicate _ -> Ok expression
    | Condition(name, message) ->
        rewriteConstructors shapes bound message
        |> Result.map (fun rewritten -> ExpressionLogic.Condition(name, rewritten))
    | Restart(name, handler) ->
        rewriteConstructors shapes bound handler
        |> Result.map (fun rewritten -> ExpressionLogic.Restart(name, rewritten))
    | Generic(name, arity, methods) ->
        rewriteAll bound methods
        |> Result.map (fun rewritten -> ExpressionLogic.Generic(name, arity, rewritten))
    | Message(kind, fields) ->
        rewriteAll bound fields
        |> Result.map (fun rewritten -> ExpressionLogic.Message(kind, rewritten))
    | Fail reason -> Ok(ExpressionLogic.Fail reason)

let private validateShapes
    (definitions: BookDefinition list)
    (shapes: (int * ShapeDeclaration) list)
    : Result<unit, ProseError> =
    let definitionNames =
        definitions |> List.map (fun definition -> definition.Name) |> Set.ofList

    let rec check (seen: Set<string>) (remaining: (int * ShapeDeclaration) list) =
        match remaining with
        | [] -> Ok()
        | (offset, shape) :: rest ->
            if seen.Contains shape.Name then
                Error(DuplicateShape offset)
            elif definitionNames.Contains shape.Name then
                Error(ShapeNameConflict offset)
            else
                check (seen.Add shape.Name) rest

    check Set.empty shapes

let private dispatchName (name: string) : string = "$dispatch:" + name

let private methodName (name: string) (index: int) : string = "$method:" + name + ":" + string index

let private validateGenerics
    (definitions: BookDefinition list)
    (shapes: ShapeDeclaration list)
    (sites: (int * GenericDeclaration) list)
    : Result<unit, ProseError> =
    let definitionNames =
        definitions |> List.map (fun definition -> definition.Name) |> Set.ofList

    let shapeNames = shapes |> List.map (fun shape -> shape.Name) |> Set.ofList

    let rec check (seen: Set<string>) (remaining: (int * GenericDeclaration) list) =
        match remaining with
        | [] -> Ok()
        | (offset, generic) :: rest ->
            if seen.Contains generic.Name then
                Error(DuplicateGeneric offset)
            elif definitionNames.Contains generic.Name || shapeNames.Contains generic.Name then
                Error(GenericNameConflict offset)
            else
                check (seen.Add generic.Name) rest

    check Set.empty sites

let private validateMethods
    (shapes: ShapeDeclaration list)
    (generics: GenericDeclaration list)
    (sites: (int * MethodDeclaration) list)
    : Result<unit, ProseError> =
    let shapeNames = shapes |> List.map (fun shape -> shape.Name) |> Set.ofList

    let arities =
        generics |> List.map (fun generic -> generic.Name, generic.Arity) |> Map.ofList

    let rec check (seen: Set<string>) (remaining: (int * MethodDeclaration) list) =
        match remaining with
        | [] -> Ok()
        | (offset, methodDeclaration) :: rest ->
            match arities.TryFind methodDeclaration.Name with
            | None -> Error(UnknownGeneric(offset, methodDeclaration.Name))
            | Some arity when methodDeclaration.Parameters.Length <> arity ->
                Error(MethodArityMismatch(offset, methodDeclaration.Name))
            | Some _ ->
                let parameterNames =
                    methodDeclaration.Parameters |> List.map (fun parameter -> parameter.Name)

                let duplicateParameter =
                    parameterNames |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1)

                match duplicateParameter with
                | Some(name, _) -> Error(DuplicateParameter(offset, name))
                | None ->
                    match
                        methodDeclaration.Parameters
                        |> List.tryFind (fun parameter -> not (shapeNames.Contains parameter.Shape))
                    with
                    | Some parameter -> Error(UnknownShape(offset, parameter.Shape))
                    | None ->
                        let key =
                            methodDeclaration.Name
                            + ":"
                            + (methodDeclaration.Parameters
                               |> List.map (fun parameter -> parameter.Shape)
                               |> String.concat ",")

                        if seen.Contains key then
                            Error(DuplicateMethod(offset, methodDeclaration.Name))
                        else
                            check (seen.Add key) rest

    check Set.empty sites

let rec private rewriteGenericCalls
    (generics: Map<string, int>)
    (expression: Expression)
    : Result<Expression, ProseError> =
    let rewrite = rewriteGenericCalls generics

    let rewriteAll (expressions: Expression list) =
        let rec loop (remaining: Expression list) (accumulated: Expression list) : Result<Expression list, ProseError> =
            match remaining with
            | [] -> Ok(List.rev accumulated)
            | next :: rest ->
                match rewrite next with
                | Error error -> Error error
                | Ok rewritten -> loop rest (rewritten :: accumulated)

        loop expressions []

    match expression with
    | Call(ExpressionLogic.Word name, arguments) when generics.ContainsKey name ->
        let arity = generics.[name]

        if arguments.Length <> arity then
            Error(GenericCallArityMismatch(name, arity, arguments.Length))
        else
            rewriteAll arguments
            |> Result.map (fun rewritten -> Call(ExpressionLogic.Word(dispatchName name), rewritten))
    | ExpressionLogic.Word name when generics.ContainsKey name && generics.[name] = 0 ->
        Ok(Call(ExpressionLogic.Word(dispatchName name), []))
    | Literal _
    | ExpressionLogic.Word _
    | Hole
    | Fail _ -> Ok expression
    | Call(target, arguments) ->
        rewrite target
        |> Result.bind (fun rewrittenTarget ->
            rewriteAll arguments
            |> Result.map (fun rewrittenArguments -> Call(rewrittenTarget, rewrittenArguments)))
    | Partial(target, arguments) ->
        rewrite target
        |> Result.bind (fun rewrittenTarget ->
            rewriteAll arguments
            |> Result.map (fun rewrittenArguments -> Partial(rewrittenTarget, rewrittenArguments)))
    | Pipeline(value, stage, style) ->
        rewrite value
        |> Result.bind (fun rewrittenValue ->
            rewrite stage
            |> Result.map (fun rewrittenStage -> Pipeline(rewrittenValue, rewrittenStage, style)))
    | ReversePipeline(target, value) ->
        rewrite target
        |> Result.bind (fun rewrittenTarget ->
            rewrite value
            |> Result.map (fun rewrittenValue -> ReversePipeline(rewrittenTarget, rewrittenValue)))
    | Quotation body -> rewriteAll body |> Result.map Quotation
    | Block body -> rewriteAll body |> Result.map Block
    | Let(name, value, body) ->
        rewrite value
        |> Result.bind (fun rewrittenValue ->
            rewrite body
            |> Result.map (fun rewrittenBody -> Let(name, rewrittenValue, rewrittenBody)))
    | Lambda(parameters, body) -> rewrite body |> Result.map (fun rewritten -> Lambda(parameters, rewritten))
    | If(condition, thenBranch, elseBranch) ->
        rewrite condition
        |> Result.bind (fun rewrittenCondition ->
            rewrite thenBranch
            |> Result.bind (fun rewrittenThen ->
                rewrite elseBranch
                |> Result.map (fun rewrittenElse -> If(rewrittenCondition, rewrittenThen, rewrittenElse))))
    | PatternMatch(subject, clauses) ->
        rewrite subject
        |> Result.bind (fun rewrittenSubject ->
            let rec loop
                (remaining: ExpressionClause list)
                (accumulated: ExpressionClause list)
                : Result<ExpressionClause list, ProseError> =
                match remaining with
                | [] -> Ok(List.rev accumulated)
                | clause :: rest ->
                    rewriteAll clause.Body
                    |> Result.bind (fun rewrittenBody ->
                        loop rest ({ clause with Body = rewrittenBody } :: accumulated))

            loop clauses []
            |> Result.map (fun rewrittenClauses -> PatternMatch(rewrittenSubject, rewrittenClauses)))
    | DeclareKind _
    | DeclarePredicate _ -> Ok expression
    | Condition(name, message) -> rewrite message |> Result.map (fun rewritten -> Condition(name, rewritten))
    | Restart(name, handler) -> rewrite handler |> Result.map (fun rewritten -> Restart(name, rewritten))
    | Generic(name, arity, methodExpressions) ->
        rewriteAll methodExpressions
        |> Result.map (fun rewritten -> Generic(name, arity, rewritten))
    | Message(kind, fields) -> rewriteAll fields |> Result.map (fun rewritten -> Message(kind, rewritten))

let private buildDispatcher
    (shapes: Map<string, int>)
    (generic: GenericDeclaration)
    (methods: MethodDeclaration list)
    : Expression =
    let reason = "NoApplicableMethod: " + generic.Name

    let argumentNames =
        [ 0 .. generic.Arity - 1 ] |> List.map (fun index -> "arg" + string index)

    let argumentWords = argumentNames |> List.map ExpressionLogic.Word

    let candidates =
        methods
        |> List.mapi (fun index methodDeclaration -> methodDeclaration, methodName generic.Name index)

    let rec build (index: int) (remaining: (MethodDeclaration * string) list) : Expression =
        if generic.Arity = 0 then
            match remaining with
            | [] -> Fail reason
            | (_, name) :: _ -> Call(ExpressionLogic.Word name, [])
        elif index >= generic.Arity then
            match remaining with
            | [] -> Fail reason
            | (_, name) :: _ -> Call(ExpressionLogic.Word name, argumentWords)
        else
            let groups =
                remaining
                |> List.groupBy (fun (methodDeclaration, _) -> methodDeclaration.Parameters.[index].Shape)

            let clauses =
                groups
                |> List.map (fun (shapeName, group) ->
                    {
                        Pattern = Constructor(shapeName, List.replicate shapes.[shapeName] PatternLogic.Wildcard)
                        Guard = None
                        Body = [ build (index + 1) group ]
                    })

            let otherwise =
                {
                    Pattern = PatternLogic.Wildcard
                    Guard = None
                    Body = [ Fail reason ]
                }

            PatternMatch(ExpressionLogic.Word argumentNames.[index], clauses @ [ otherwise ])

    build 0 candidates

/// Parses one `.prose` program: a sequence of sentences. A single sentence
/// keeps its expression; two or more become a `Block` in source order. Names
/// are lowercase surface words canonicalized through the vocabulary; a nested
/// match is closed by `close`.
let private parseProgramWithRanges
    (bookMode: bool)
    (vocabulary: VocabularyEntry list)
    (text: string)
    : Result<
        Expression *
        ExpressionSourceMap *
        BookDefinition list *
        string list *
        ShapeDeclaration list *
        GenericDeclaration list *
        MethodDeclaration list,
        ProseError
       >
    =
    match lexRaw text with
    | Error error -> Error error
    | Ok raw ->
        match combine vocabulary raw with
        | Error error -> Error error
        | Ok tokens ->
            let cursor =
                {
                    Tokens = tokens
                    Source = text
                    EndOffset = text.Length
                    Index = 0
                    Ranges = ResizeArray<ExpressionPath * SourceRange>()
                }

            let definitions = ResizeArray<BookDefinition>()
            let opens = ResizeArray<string>()
            let shapes = ResizeArray<ShapeDeclaration>()
            let shapeSites = ResizeArray<int * ShapeDeclaration>()
            let generics = ResizeArray<GenericDeclaration>()
            let genericSites = ResizeArray<int * GenericDeclaration>()
            let methods = ResizeArray<MethodDeclaration>()
            let methodSites = ResizeArray<int * MethodDeclaration>()

            let rec sentences (accumulated: Expression list) : Result<Expression, ProseError> =
                let token = tokenAt cursor
                let offset = offsetAt cursor

                match token with
                | End ->
                    if bookMode then
                        if definitions.Count = 0 then
                            Error(EmptyBook offset)
                        else
                            Ok(ExpressionLogic.Literal(ExpressionLogic.NumberLiteral 0.0))
                    else
                        match List.rev accumulated with
                        | [] -> Error(EmptyProgram offset)
                        | [ single ] -> Ok single
                        | many -> Ok(ExpressionLogic.Block many)
                | Keyword marker when bookMode && (marker = "logic" || marker = "cycle") ->
                    let bookMarker =
                        match marker with
                        | "logic" -> BookMarker.Logic
                        | _ -> BookMarker.Cycle

                    match parseLogic bookMarker () with
                    | Error error -> Error error
                    | Ok definition ->
                        let expressions = List.rev accumulated @ [ definition ]

                        Ok(
                            if expressions.Length = 1 then
                                definition
                            else
                                ExpressionLogic.Block expressions
                        )
                | Keyword "open" when bookMode ->
                    if definitions.Count > 0 then
                        Error(OpenAfterDefinition offset)
                    else
                        advance cursor

                        match tokenAt cursor with
                        | Name openName ->
                            advance cursor
                            opens.Add openName
                            sentences accumulated
                        | _ -> Error(ExpectedOpenName(offsetAt cursor))
                | Keyword "shape" when bookMode ->
                    let startOffset = offset

                    match parseShape () with
                    | Error error -> Error error
                    | Ok declaration ->
                        shapes.Add declaration
                        shapeSites.Add(startOffset, declaration)
                        sentences accumulated
                | Keyword "shape" -> Error(ShapeOutsideBook offset)
                | Keyword "generic" when bookMode ->
                    let startOffset = offset

                    match parseGeneric () with
                    | Error error -> Error error
                    | Ok declaration ->
                        generics.Add declaration
                        genericSites.Add(startOffset, declaration)
                        sentences accumulated
                | Keyword "method" when bookMode ->
                    let startOffset = offset

                    match parseMethod () with
                    | Error error -> Error error
                    | Ok declaration ->
                        methods.Add declaration
                        methodSites.Add(startOffset, declaration)
                        sentences accumulated
                | Keyword "generic" -> Error(GenericOutsideBook offset)
                | Keyword "method" -> Error(MethodOutsideBook offset)
                | Keyword "logic" when not bookMode && nextTokenIs LeftParenthesis cursor ->
                    match parseExpression cursor [] with
                    | Error error -> Error error
                    | Ok expression ->
                        match tokenAt cursor with
                        | Period ->
                            advance cursor
                            sentences (expression :: accumulated)
                        | _ -> Error(ExpectedPeriod(offsetAt cursor))
                | Keyword "logic"
                | Keyword "cycle" -> Error(LogicOutsideBook offset)
                | Keyword "open" -> Error(OpenOutsideBook offset)
                | _ when bookMode -> Error(UnexpectedSentence offset)
                | Name "run"
                | Name "capture"
                | Name "feed"
                | Name "save" when startsCommand cursor ->
                    match parseCommand cursor [] with
                    | Error error -> Error error
                    | Ok expression ->
                        match tokenAt cursor with
                        | Period ->
                            advance cursor
                            sentences (expression :: accumulated)
                        | _ -> Error(ExpectedPeriod(offsetAt cursor))
                | Keyword "match" ->
                    match parseMatch false cursor [] with
                    | Error error -> Error error
                    | Ok expression -> sentences (expression :: accumulated)
                | Keyword "when"
                | Keyword "otherwise" -> Error(ExpectedMatch offset)
                | Keyword "close" -> Error(UnexpectedClose offset)
                | _ ->
                    match parseExpression cursor [] with
                    | Error error -> Error error
                    | Ok expression ->
                        match tokenAt cursor with
                        | Period ->
                            advance cursor
                            sentences (expression :: accumulated)
                        | _ -> Error(ExpectedPeriod(offsetAt cursor))

            and parseDefinition (marker: BookMarker) () : Result<string * Expression, ProseError> =
                let startOffset = offsetAt cursor

                match tokenAt cursor with
                | Name name ->
                    advance cursor

                    let rec parameters (accumulated: string list) : string list =
                        match tokenAt cursor with
                        | Name parameter ->
                            advance cursor
                            parameters (parameter :: accumulated)
                        | _ -> List.rev accumulated

                    let parameterNames = parameters []

                    match tokenAt cursor with
                    | Equals ->
                        advance cursor

                        match parseExpression cursor [] with
                        | Error error -> Error error
                        | Ok body ->
                            match tokenAt cursor with
                            | Period ->
                                advance cursor

                                definitions.Add(
                                    {
                                        Name = name
                                        Parameters = parameterNames
                                        Body = body
                                        Marker = marker
                                        Synthetic = false
                                    }
                                )

                                recordRange cursor [] startOffset (offsetAt cursor)

                                let value =
                                    match parameterNames with
                                    | [] -> body
                                    | names -> ExpressionLogic.Lambda(names, body)

                                Ok(name, value)
                            | _ -> Error(ExpectedPeriod(offsetAt cursor))
                    | _ -> Error(ExpectedEquals(offsetAt cursor))
                | _ -> Error(ExpectedBindingName(offsetAt cursor))

            and parseLogic (marker: BookMarker) () : Result<Expression, ProseError> =
                advance cursor

                let rec group (accumulated: (string * Expression) list) : Result<Expression, ProseError> =
                    match parseDefinition marker () with
                    | Error error -> Error error
                    | Ok definition ->
                        let accumulated = accumulated @ [ definition ]

                        match tokenAt cursor with
                        | Name "and" when marker = BookMarker.Cycle ->
                            advance cursor
                            group accumulated
                        | _ ->
                            match sentences [] with
                            | Error error -> Error error
                            | Ok rest ->
                                Ok(
                                    List.foldBack
                                        (fun (name, value) body -> ExpressionLogic.Let(name, value, body))
                                        accumulated
                                        rest
                                )

                group []

            and parseShape () : Result<ShapeDeclaration, ProseError> =
                advance cursor

                match tokenAt cursor with
                | Name shapeName ->
                    advance cursor

                    match tokenAt cursor with
                    | Period ->
                        advance cursor
                        Ok { Name = shapeName; Fields = [] }
                    | Keyword "with" ->
                        advance cursor

                        let rec fields (accumulated: string list) : Result<string list, ProseError> =
                            match tokenAt cursor with
                            | Name field ->
                                advance cursor
                                fields (field :: accumulated)
                            | Period ->
                                advance cursor
                                Ok(List.rev accumulated)
                            | End -> Error(ExpectedPeriod(offsetAt cursor))
                            | _ -> Error(ExpectedShapeFields(offsetAt cursor))

                        match fields [] with
                        | Ok [] -> Error(ExpectedShapeFields(offsetAt cursor))
                        | Ok names -> Ok { Name = shapeName; Fields = names }
                        | Error error -> Error error
                    | _ -> Error(ExpectedShapeFields(offsetAt cursor))
                | _ -> Error(ExpectedShapeName(offsetAt cursor))

            and parseGeneric () : Result<GenericDeclaration, ProseError> =
                advance cursor

                match tokenAt cursor with
                | Name name ->
                    advance cursor

                    match tokenAt cursor with
                    | Keyword "with" ->
                        advance cursor

                        match tokenAt cursor with
                        | Number value when
                            value >= 0.0
                            && value = System.Math.Floor value
                            && value <= float System.Int32.MaxValue
                            ->
                            advance cursor

                            match tokenAt cursor with
                            | Period ->
                                advance cursor
                                Ok { Name = name; Arity = int value }
                            | _ -> Error(ExpectedPeriod(offsetAt cursor))
                        | Number value -> Error(InvalidGenericArity(offsetAt cursor, string value))
                        | _ -> Error(ExpectedGenericArity(offsetAt cursor))
                    | _ -> Error(ExpectedGenericArity(offsetAt cursor))
                | _ -> Error(ExpectedGenericName(offsetAt cursor))

            and parseMethod () : Result<MethodDeclaration, ProseError> =
                advance cursor

                match tokenAt cursor with
                | Name name ->
                    advance cursor

                    match tokenAt cursor with
                    | LeftParenthesis ->
                        advance cursor

                        let rec parameters
                            (accumulated: MethodParameter list)
                            : Result<MethodParameter list, ProseError> =
                            match tokenAt cursor with
                            | RightParenthesis ->
                                advance cursor
                                Ok(List.rev accumulated)
                            | Name parameterName ->
                                advance cursor

                                match tokenAt cursor with
                                | Colon ->
                                    advance cursor

                                    match tokenAt cursor with
                                    | Name shapeName ->
                                        advance cursor

                                        parameters (
                                            {
                                                Name = parameterName
                                                Shape = shapeName
                                            }
                                            :: accumulated
                                        )
                                    | _ -> Error(ExpectedParameterShape(offsetAt cursor))
                                | _ -> Error(ExpectedParameterColon(offsetAt cursor))
                            | _ -> Error(ExpectedParameterName(offsetAt cursor))

                        match parameters [] with
                        | Error error -> Error error
                        | Ok parsedParameters ->
                            match tokenAt cursor with
                            | Equals ->
                                advance cursor

                                match parseExpression cursor [] with
                                | Error error -> Error error
                                | Ok body ->
                                    match tokenAt cursor with
                                    | Period ->
                                        advance cursor

                                        Ok
                                            {
                                                Name = name
                                                Parameters = parsedParameters
                                                Body = body
                                            }
                                    | _ -> Error(ExpectedPeriod(offsetAt cursor))
                            | _ -> Error(ExpectedEquals(offsetAt cursor))
                    | _ -> Error(ExpectedMethodParameters(offsetAt cursor))
                | _ -> Error(ExpectedMethodName(offsetAt cursor))

            sentences []
            |> Result.bind (fun expression ->
                let ranges = Map.ofList (List.ofSeq cursor.Ranges)

                let ranges =
                    if Map.containsKey [] ranges then
                        ranges
                    else
                        Map.add
                            []
                            {
                                StartOffset = 0
                                EndOffset = text.Length
                            }
                            ranges

                let parsedDefinitions = List.ofSeq definitions
                let parsedShapes = List.ofSeq shapes
                let parsedOpens = List.ofSeq opens
                let parsedGenerics = List.ofSeq generics
                let parsedMethods = List.ofSeq methods

                if not bookMode then
                    Ok(expression, ranges, parsedDefinitions, parsedOpens, parsedShapes, parsedGenerics, parsedMethods)
                else
                    validateShapes parsedDefinitions (List.ofSeq shapeSites)
                    |> Result.bind (fun () ->
                        validateGenerics parsedDefinitions parsedShapes (List.ofSeq genericSites)
                        |> Result.bind (fun () ->
                            validateMethods parsedShapes parsedGenerics (List.ofSeq methodSites)
                            |> Result.bind (fun () ->
                                let fieldCounts =
                                    parsedShapes
                                    |> List.map (fun shape -> shape.Name, List.length shape.Fields)
                                    |> Map.ofList

                                let genericArities =
                                    parsedGenerics
                                    |> List.map (fun generic -> generic.Name, generic.Arity)
                                    |> Map.ofList

                                let syntheticMethods =
                                    parsedMethods
                                    |> List.mapi (fun index methodDeclaration ->
                                        {
                                            Name = methodName methodDeclaration.Name index
                                            Parameters =
                                                methodDeclaration.Parameters
                                                |> List.map (fun parameter -> parameter.Name)
                                            Body = methodDeclaration.Body
                                            Marker = BookMarker.Logic
                                            Synthetic = true
                                        })

                                let syntheticDispatchers =
                                    parsedGenerics
                                    |> List.map (fun generic ->
                                        {
                                            Name = dispatchName generic.Name
                                            Parameters =
                                                [ 0 .. generic.Arity - 1 ]
                                                |> List.map (fun index -> "arg" + string index)
                                            Body =
                                                buildDispatcher
                                                    fieldCounts
                                                    generic
                                                    (parsedMethods
                                                     |> List.filter (fun methodDeclaration ->
                                                         methodDeclaration.Name = generic.Name))
                                            Marker = BookMarker.Logic
                                            Synthetic = true
                                        })

                                let allDefinitions = syntheticMethods @ syntheticDispatchers @ parsedDefinitions

                                let rewriteDefinition (definition: BookDefinition) =
                                    rewriteGenericCalls genericArities definition.Body
                                    |> Result.bind (fun body ->
                                        rewriteConstructors fieldCounts (Set.ofList definition.Parameters) body)
                                    |> Result.map (fun body -> { definition with Body = body })

                                let rec rewriteDefinitions
                                    (remaining: BookDefinition list)
                                    (accumulated: BookDefinition list)
                                    =
                                    match remaining with
                                    | [] -> Ok(List.rev accumulated)
                                    | definition :: rest ->
                                        match rewriteDefinition definition with
                                        | Error error -> Error error
                                        | Ok rewritten -> rewriteDefinitions rest (rewritten :: accumulated)

                                rewriteGenericCalls genericArities expression
                                |> Result.bind (fun program -> rewriteConstructors fieldCounts Set.empty program)
                                |> Result.bind (fun rewrittenProgram ->
                                    rewriteDefinitions allDefinitions []
                                    |> Result.map (fun rewrittenDefinitions ->
                                        (rewrittenProgram,
                                         ranges,
                                         rewrittenDefinitions,
                                         parsedOpens,
                                         parsedShapes,
                                         parsedGenerics,
                                         parsedMethods)))))))

let parseProgram (vocabulary: VocabularyEntry list) (text: string) : Result<Expression, ProseError> =
    parseProgramWithRanges false vocabulary text
    |> Result.map (fun (expression, _, _, _, _, _, _) -> expression)

let parseLocated
    (vocabulary: VocabularyEntry list)
    (text: string)
    : Result<Expression * ExpressionSourceMap, ProseError> =
    parseProgramWithRanges false vocabulary text
    |> Result.map (fun (expression, ranges, _, _, _, _, _) -> expression, ranges)

/// Parses a `.book` file: `logic` definitions and `shape` declarations. A book
/// without definitions is `EmptyBook`; a free sentence is `UnexpectedSentence`.
let parseBook (vocabulary: VocabularyEntry list) (text: string) : Result<BookDocument, ProseError> =
    parseProgramWithRanges true vocabulary text
    |> Result.map (fun (program, _, definitions, opens, shapes, generics, methods) ->
        {
            Definitions = definitions
            Shapes = shapes
            Generics = generics
            Methods = methods
            Opens = opens
            Program = program
        })
