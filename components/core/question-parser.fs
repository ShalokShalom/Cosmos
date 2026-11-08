module QuestionParser

// ============================================================
// The Question surface parser. Symbols carry the structure:
//   fact.                 (fact)
//   head :- goal, goal.   (rule)
//   ?- goal, goal.        (question)
// Terms use the shared PatternLogic.Pattern. Execution is the next slice.
// ============================================================

open System
open System.Globalization
open PatternLogic
open QuestionLogic

type QuestionError =
    | EmptyProgram of offset: int
    | ExpectedTerm of offset: int
    | ExpectedOpenParenthesis of offset: int
    | ExpectedCloseParenthesis of offset: int
    | ExpectedPeriod of offset: int
    | ExpectedGoal of offset: int
    | DuplicateQuestion of offset: int
    | ClauseAfterQuestion of offset: int
    | ExpectedVariableName of offset: int
    | UnclosedText of offset: int
    | InvalidNumber of offset: int * text: string
    | UnexpectedCharacter of offset: int * text: string

let describeError (error: QuestionError) : string =
    let offset, message =
        match error with
        | EmptyProgram at -> at, "a question program needs a clause or a question"
        | ExpectedTerm at -> at, "a term is required"
        | ExpectedOpenParenthesis at -> at, "a compound term needs ("
        | ExpectedCloseParenthesis at -> at, "a compound term needs )"
        | ExpectedPeriod at -> at, "a clause ends with a period"
        | ExpectedGoal at -> at, "a rule or question needs at least one goal"
        | DuplicateQuestion at -> at, "a question program has one question"
        | ClauseAfterQuestion at -> at, "the question must be the last item"
        | ExpectedVariableName at -> at, "a question variable needs a name after ?"
        | UnclosedText at -> at, "a text literal is not closed"
        | InvalidNumber(at, text) -> at, "invalid number " + text
        | UnexpectedCharacter(at, text) -> at, "unexpected character " + text

    message + " at offset " + string offset

type private Token =
    | Name of text: string
    | Variable of text: string
    | Number of value: float
    | Text of value: string
    | Wildcard
    | LeftParenthesis
    | RightParenthesis
    | Comma
    | Period
    | ColonDash
    | QuestionDash
    | End

type private Lexeme = { Token: Token; Offset: int }

type private Cursor =
    {
        Tokens: Lexeme array
        EndOffset: int
        mutable Index: int
    }

let private advance (cursor: Cursor) : unit = cursor.Index <- cursor.Index + 1

let private peek (cursor: Cursor) : Lexeme =
    if cursor.Index < cursor.Tokens.Length then
        cursor.Tokens.[cursor.Index]
    else
        {
            Token = End
            Offset = cursor.EndOffset
        }

let private tokenAt (cursor: Cursor) : Token = (peek cursor).Token
let private offsetAt (cursor: Cursor) : int = (peek cursor).Offset

let private lex (text: string) : Result<Lexeme array, QuestionError> =
    let tokens = ResizeArray<Lexeme>()
    let mutable index = 0
    let mutable error = None
    let add token offset = tokens.Add { Token = token; Offset = offset }

    let readName (start: int) : int * string =
        let mutable finish = start

        while finish < text.Length && (Char.IsLetterOrDigit text.[finish] || text.[finish] = '_') do
            finish <- finish + 1

        finish, text.Substring(start, finish - start)

    let readNumber (start: int) : int * string =
        let mutable finish = if text.[start] = '-' then start + 1 else start

        while finish < text.Length && Char.IsDigit text.[finish] do
            finish <- finish + 1

        if finish + 1 < text.Length && text.[finish] = '.' && Char.IsDigit text.[finish + 1] then
            finish <- finish + 1

            while finish < text.Length && Char.IsDigit text.[finish] do
                finish <- finish + 1

        finish, text.Substring(start, finish - start)

    while index < text.Length && error.IsNone do
        let character = text.[index]

        if Char.IsWhiteSpace character then
            index <- index + 1
        elif character = '%' then
            while index < text.Length && text.[index] <> '\n' do
                index <- index + 1
        else
            match character with
            | '(' ->
                add LeftParenthesis index
                index <- index + 1
            | ')' ->
                add RightParenthesis index
                index <- index + 1
            | ',' ->
                add Comma index
                index <- index + 1
            | '.' ->
                add Period index
                index <- index + 1
            | ':' when index + 1 < text.Length && text.[index + 1] = '-' ->
                add ColonDash index
                index <- index + 2
            | '?' when index + 1 < text.Length && text.[index + 1] = '-' ->
                add QuestionDash index
                index <- index + 2
            | '?' ->
                let start = index
                index <- index + 1

                if index >= text.Length || not (Char.IsLetter text.[index]) then
                    error <- Some(ExpectedVariableName start)
                else
                    let finish, name = readName index
                    add (Variable name) start
                    index <- finish
            | '_' ->
                add Wildcard index
                index <- index + 1
            | '"' ->
                let start = index
                let mutable finish = index + 1
                let mutable closed = false

                while finish < text.Length && not closed do
                    if text.[finish] = '"' then
                        closed <- true
                    else
                        finish <- finish + 1

                if not closed then
                    error <- Some(UnclosedText start)
                else
                    add (Text(text.Substring(index + 1, finish - index - 1))) start
                    index <- finish + 1
            | character when Char.IsLetter character ->
                let start = index
                let finish, name = readName index
                add (Name name) start
                index <- finish
            | character when
                Char.IsDigit character
                || (character = '-'
                    && index + 1 < text.Length
                    && Char.IsDigit text.[index + 1])
                ->
                let start = index
                let finish, raw = readNumber index

                match Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, value when Double.IsFinite value ->
                    add (Number value) start
                    index <- finish
                | _ ->
                    error <- Some(InvalidNumber(start, raw))
            | character ->
                error <- Some(UnexpectedCharacter(index, string character))
                index <- index + 1

    match error with
    | Some questionError -> Error questionError
    | None -> Ok(tokens.ToArray())

let rec private parseTerm (cursor: Cursor) : Result<Pattern, QuestionError> =
    match tokenAt cursor with
    | Name name ->
        let start = offsetAt cursor
        advance cursor

        match tokenAt cursor with
        | LeftParenthesis ->
            advance cursor
            parseArguments cursor start []
            |> Result.map (fun arguments -> Constructor(name, arguments))
        | _ -> Ok(AtomLiteral name)
    | Variable name ->
        advance cursor
        Ok(PatternLogic.Variable(name, PatternLogic.Fresh))
    | Wildcard ->
        advance cursor
        Ok PatternLogic.Wildcard
    | Number value ->
        advance cursor
        Ok(NumberLiteral value)
    | Text value ->
        advance cursor
        Ok(TextLiteral value)
    | _ -> Error(ExpectedTerm(offsetAt cursor))
    |> Result.bind (fun term ->
        match term with
        | Constructor _
        | AtomLiteral _
        | PatternLogic.Variable _
        | PatternLogic.Wildcard
        | NumberLiteral _
        | TextLiteral _ -> Ok term
        | TypedPattern _ -> Ok term)

and private parseArguments
    (cursor: Cursor)
    (start: int)
    (accumulated: Pattern list)
    : Result<Pattern list, QuestionError> =
    match tokenAt cursor with
    | RightParenthesis ->
        advance cursor
        Ok(List.rev accumulated)
    | End -> Error(ExpectedCloseParenthesis start)
    | _ ->
        parseTerm cursor
        |> Result.bind (fun argument ->
            match tokenAt cursor with
            | Comma ->
                advance cursor
                parseArguments cursor start (argument :: accumulated)
            | RightParenthesis ->
                advance cursor
                Ok(List.rev (argument :: accumulated))
            | _ -> Error(ExpectedCloseParenthesis(offsetAt cursor)))

let rec private parseGoals (cursor: Cursor) : Result<Pattern list, QuestionError> =
    parseTerm cursor
    |> Result.bind (fun goal ->
        let rec rest (accumulated: Pattern list) : Result<Pattern list, QuestionError> =
            match tokenAt cursor with
            | Comma ->
                advance cursor
                let nextOffset = offsetAt cursor

                if tokenAt cursor = Period || tokenAt cursor = End then
                    Error(ExpectedGoal nextOffset)
                else
                    parseTerm cursor
                    |> Result.bind (fun next -> rest (next :: accumulated))
            | _ -> Ok(List.rev accumulated)

        rest [ goal ])

let private parseClause (cursor: Cursor) : Result<QuestionClause, QuestionError> =
    parseTerm cursor
    |> Result.bind (fun head ->
        match tokenAt cursor with
        | ColonDash ->
            advance cursor
            parseGoals cursor
            |> Result.bind (fun body ->
                match tokenAt cursor with
                | Period ->
                    advance cursor
                    Ok { Head = head; Body = body }
                | _ -> Error(ExpectedPeriod(offsetAt cursor)))
        | Period ->
            advance cursor
            Ok { Head = head; Body = [] }
        | _ -> Error(ExpectedPeriod(offsetAt cursor)))

let parse (text: string) : Result<QuestionProgram, QuestionError> =
    match lex text with
    | Error error -> Error error
    | Ok tokens ->
        let cursor =
            {
                Tokens = tokens
                EndOffset = text.Length
                Index = 0
            }

        let clauses = ResizeArray<QuestionClause>()
        let mutable question: Pattern list option = None
        let mutable error = None

        while error.IsNone && tokenAt cursor <> End do
            match tokenAt cursor with
            | QuestionDash ->
                let start = offsetAt cursor

                if question.IsSome then
                    error <- Some(DuplicateQuestion start)
                else
                    advance cursor

                    match parseGoals cursor with
                    | Error questionError -> error <- Some questionError
                    | Ok goals ->
                        match tokenAt cursor with
                        | Period ->
                            advance cursor
                            question <- Some goals
                        | _ -> error <- Some(ExpectedPeriod(offsetAt cursor))
            | _ when question.IsSome -> error <- Some(ClauseAfterQuestion(offsetAt cursor))
            | _ ->
                match parseClause cursor with
                | Error questionError -> error <- Some questionError
                | Ok clause ->
                    clauses.Add clause
                    if question.IsSome then error <- Some(ClauseAfterQuestion(offsetAt cursor))

        match error with
        | Some questionError -> Error questionError
        | None when clauses.Count = 0 && question.IsNone -> Error(EmptyProgram 0)
        | None ->
            Ok
                {
                    Clauses = List.ofSeq clauses
                    Question = question
                }
