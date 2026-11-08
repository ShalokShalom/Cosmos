module QuestionLogic

// ============================================================
// Question profile. Facts and rules are clauses; a question is a goal list.
// The term shape is the shared PatternLogic.Pattern, so Question does not add
// a second term model.
// ============================================================

open PatternLogic

/// One fact or rule. An empty body is a fact.
type QuestionClause =
    {
        Head: Pattern
        Body: Pattern list
    }

/// A parsed question program: clauses followed by an optional goal list.
type QuestionProgram =
    {
        Clauses: QuestionClause list
        Question: Pattern list option
    }
