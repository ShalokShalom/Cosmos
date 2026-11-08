module PackageLogic

// ============================================================
// Container profile. A package is shared by Logic books and future Question
// datasets; it is a container, not a third executable IR.
// ============================================================

open PatternLogic
open ExpressionLogic

/// A package dependency. The version is exact at this layer; compatibility
/// policy belongs to the loader.
type PackageImport =
    {
        Name: string
        Version: string
    }

/// An exported name and the unit that owns it.
type PackageExport =
    {
        Name: string
        Unit: string
    }

/// How a binding was classified by the source layer. The code names are the
/// schema values; a later slice maps F\#'s read-off onto them.
type BindingKind =
    | NonRecursiveFunction
    | RecursiveFunction
    | MutuallyRecursiveFunction of string list
    | NonRecursiveValue
    | RecursiveValue

/// One classified binding in a unit group.
type BindingClassification =
    {
        Name: string
        Kind: BindingKind
    }

/// One strongly connected definition group. `InitOrder` is the value-definition
/// order; the classification is checked against the group's unit set.
type PackageUnitGroup =
    {
        Name: string
        Units: string list
        InitOrder: string list
        Classification: BindingClassification list
    }

/// One hot-reload unit. The source hash and dependency set are its identity.
/// `Arity` and `Captures` are explicit, not inferred from the root shape.
type PackageUnit =
    {
        Name: string
        SourceHash: string
        Dependencies: string list
        Arity: int
        Captures: int list
        Root: Expression
    }

/// A closed, versioned package document. Kinds and predicates are the pinned
/// registries the units reference; units are ordered; groups carry recursion
/// metadata for the runtime.
type PackageDocument =
    {
        SchemaVersion: int
        Form: string
        Name: string
        Version: string
        SourceHash: string
        Imports: PackageImport list
        Exports: PackageExport list
        Capabilities: string list
        Kinds: PatternKind list
        Predicates: (string * int) list
        Units: PackageUnit list
        Groups: PackageUnitGroup list
    }
