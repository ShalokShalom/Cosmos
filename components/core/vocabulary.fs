module Vocabulary

open Types

let gameVocabulary: VocabularyEntry list =
    [
        {
            Token = "player"
            Form = "player"
            ImplementationName = "Player"
            Category = TypeName
            IsActive = true
        }
        {
            Token = "enemy"
            Form = "enemy"
            ImplementationName = "Enemy"
            Category = TypeName
            IsActive = true
        }
        {
            Token = "score"
            Form = "score"
            ImplementationName = "Score"
            Category = TypeName
            IsActive = true
        }
        {
            Token = "alive"
            Form = "alive"
            ImplementationName = "Alive"
            Category = FieldName
            IsActive = true
        }
        {
            Token = "health"
            Form = "health"
            ImplementationName = "Health"
            Category = FieldName
            IsActive = true
        }
        {
            Token = "position"
            Form = "position"
            ImplementationName = "Position"
            Category = FieldName
            IsActive = true
        }
        {
            Token = "isAlive"
            Form = "is alive"
            ImplementationName = "Alive"
            Category = FieldName
            IsActive = true
        }
        {
            Token = "jump"
            Form = "jump"
            ImplementationName = "jump"
            Category = FunctionName
            IsActive = true
        }
        {
            Token = "run"
            Form = "run"
            ImplementationName = "run"
            Category = FunctionName
            IsActive = true
        }
        {
            Token = "land"
            Form = "land"
            ImplementationName = "land"
            Category = FunctionName
            IsActive = true
        }
        {
            Token = "canJump"
            Form = "can jump"
            ImplementationName = "canJump"
            Category = FunctionName
            IsActive = true
        }
        {
            Token = "canRun"
            Form = "can run"
            ImplementationName = "canRun"
            Category = FunctionName
            IsActive = true
        }
        {
            Token = "canFly"
            Form = "can fly"
            ImplementationName = "canFly"
            Category = FunctionName
            IsActive = false
        }
        {
            Token = "every"
            Form = "every"
            ImplementationName = "every"
            Category = Quantifier
            IsActive = true
        }
        {
            Token = "all"
            Form = "all"
            ImplementationName = "all"
            Category = Quantifier
            IsActive = true
        }
        {
            Token = "some"
            Form = "some"
            ImplementationName = "some"
            Category = Quantifier
            IsActive = true
        }
        {
            Token = "no"
            Form = "no"
            ImplementationName = "no"
            Category = Quantifier
            IsActive = true
        }
        {
            Token = "that"
            Form = "that"
            ImplementationName = "that"
            Category = Connective
            IsActive = true
        }
        {
            Token = "and"
            Form = "and"
            ImplementationName = "and"
            Category = Connective
            IsActive = true
        }
        {
            Token = "or"
            Form = "or"
            ImplementationName = "or"
            Category = Connective
            IsActive = true
        }
        {
            Token = "is"
            Form = "is"
            ImplementationName = "is"
            Category = Copula
            IsActive = true
        }
        {
            Token = "has"
            Form = "has"
            ImplementationName = "has"
            Category = Copula
            IsActive = true
        }
        {
            Token = "greaterThan"
            Form = "greater than"
            ImplementationName = "greaterThan"
            Category = Comparison
            IsActive = true
        }
        {
            Token = "atLeast"
            Form = "at least"
            ImplementationName = "atLeast"
            Category = Comparison
            IsActive = true
        }
        {
            Token = "lessThan"
            Form = "less than"
            ImplementationName = "lessThan"
            Category = Comparison
            IsActive = true
        }
        {
            Token = "atMost"
            Form = "at most"
            ImplementationName = "atMost"
            Category = Comparison
            IsActive = true
        }
        {
            Token = "equalTo"
            Form = "equal to"
            ImplementationName = "equalTo"
            Category = Comparison
            IsActive = true
        }
        {
            Token = "when"
            Form = "when"
            ImplementationName = "when"
            Category = Conditional
            IsActive = true
        }
        {
            Token = "never"
            Form = "never"
            ImplementationName = "never"
            Category = Negation
            IsActive = true
        }
        {
            Token = "not"
            Form = "not"
            ImplementationName = "not"
            Category = Negation
            IsActive = true
        }
        {
            Token = "the"
            Form = "the"
            ImplementationName = "the"
            Category = Article
            IsActive = true
        }
        {
            Token = "a"
            Form = "a"
            ImplementationName = "a"
            Category = Article
            IsActive = true
        }
    ]
