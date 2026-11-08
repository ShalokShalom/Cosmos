module PatternLogic

// ============================================================
// Shared pattern profile. Patterns are used by Action matches, Question
// unification, and Logic match clauses; this is not a fourth language or a
// per-surface codec.
// ============================================================

open Types

/// The executable variable roles. Future hosts elaborate their own roles into
/// combinations of these four.
type VariableRole =
    | Fresh
    | Pin
    | Repeated
    | Shadowing

/// A constructor-headed runtime pattern. Functions are not patterns.
type Pattern =
    | Wildcard
    | Variable of name: string * role: VariableRole
    | NumberLiteral of value: RealNumber
    | TextLiteral of value: string
    | AtomLiteral of value: string
    | Constructor of name: string * children: Pattern list
    | TypedPattern of sortName: string * child: Pattern

/// A guard is a named predicate with a pinned version. A host-expression guard
/// is not portable and never appears in this form.
type PatternGuard =
    {
        Predicate: string
        Version: int
    }

/// One ordered clause. The body is surface text; the IR owns only the pattern
/// and the guard reference.
type PatternClause =
    {
        Pattern: Pattern
        Guard: PatternGuard option
        Body: string
    }

/// A canonical constructor registration: a name and its fixed arity.
type PatternKind =
    {
        Name: string
        Arity: int
    }

/// A closed, versioned pattern document.
type PatternDocument =
    {
        SchemaVersion: int
        Form: string
        Kinds: PatternKind list
        Predicates: (string * int) list
        Clauses: PatternClause list
    }

/// A declared hole in a pattern template. The hole names the kind it expects;
/// "any" is the unconstrained hole. Holes are elaboration-time only.
type HoleSignature =
    {
        Name: string
        Kind: string
    }

/// An open pattern template. It is not executable and is never loaded as a
/// runtime clause; elaboration replaces every hole with a closed sub-pattern.
type TemplateNode =
    | TemplateHole of name: string
    | TemplateVariable of name: string * role: VariableRole
    | TemplateNumber of value: RealNumber
    | TemplateText of value: string
    | TemplateAtom of value: string
    | TemplateConstructor of name: string * children: TemplateNode list
    | TemplateTyped of sortName: string * child: TemplateNode

/// A versioned template document. The holes are the declared interface; the
/// root is the open pattern body.
type PatternTemplate =
    {
        SchemaVersion: int
        Form: string
        Holes: HoleSignature list
        Kinds: PatternKind list
        Root: TemplateNode
    }
