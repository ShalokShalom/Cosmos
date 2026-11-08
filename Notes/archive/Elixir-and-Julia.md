# A Unified Language for Transpiling Julia and Elixir

## Purpose

This document specifies the design of a programming language that serves as a **common transpilation target** for Julia and Elixir source code. It is a purpose-built language with a **dedicated runtime**; it is not tied to any existing virtual machine, and the runtime is a black box as far as this document is concerned. What this document specifies is **language semantics** — the constructs, their meanings, and the rules that keep the two source languages' semantics from leaking into each other.

The language is designed for **100% coverage of both ecosystems**. Every construct in either source language is hosted with defined semantics. Source that uses a construct is translated into that construct. No construct is rejected as "out of scope." Where the two source languages have *different* semantics for what looks like the same construct, the language provides **two visibly distinct constructs**, so a reader knows from the token alone which semantics apply.

The goal is a language that:

- expresses **every** construct in both source languages without loss,
- reads naturally to a programmer of either source language,
- runs on a dedicated runtime that provides the primitives both languages need,
- and never requires the reader to know *which* source language a construct came from in order to understand it.

The guiding principle throughout is:

> **If a programmer cannot tell from the keyword or operator alone which source language's semantics apply, the construct is not clean enough.**

Concretely: **separate constructs whose semantics differ** (dispatch models, error channels, mutability, comparison, concurrency, macro systems), and **combine constructs whose semantics are identical** (pipes, tuples, interpolation, comprehensions), lifting the better surface from whichever source language has it and letting the other get it for free.

---

## Part 1 — The two dispatch constructs

Elixir's `def` and Julia's `function` are superficially similar but semantically disjoint. Elixir clauses are **ordered**, **first-match-wins**, **pattern-and-guard based**, and **closed** (all clauses live in one module). Julia methods are **unordered**, **specificity-resolved**, **type-annotation based**, and **open** (methods can be added from any module). Forcing both into one construct requires a per-clause discriminator that is fragile under refactoring and tooling.

The language therefore provides **two distinct constructs**:

| | `method` | `clause` |
|---|---|---|
| Parameter type annotation `x: Type` | required for dispatch | **error** — types forbidden |
| Parameter pattern `%{k: v}`, `[h \| t]`, `0` | **error** — patterns forbidden | allowed |
| Guard `when …` | **error** — guards forbidden | allowed |
| `where T <: S` constraint | allowed — participates in dispatch | **error** — constraints forbidden |
| Resolution order | most specific wins | first match wins |
| Source order matters | no | yes |
| Default extensibility | **open** (Julia) | **closed** (Elixir) |
| Ambiguity possible | yes (incomparable signatures) | no (linear scan) |
| Translates | Julia `function` / Elixir `defimpl` | Elixir `def` |

The keyword is the model. No modifier, formatter, or refactor can flip one into the other. Type annotations are never ambiguous with patterns, because the two constructs accept disjoint parameter grammars.

### 1.1 `method` — typed dispatch

```text
method Area.area(shape: Circle)
    π * shape.radius ** 2
end

method Area.area(shape: Rectangle)
    shape.width * shape.height
end

method Area.area(shape: Any)
    throw(ArgumentError("unsupported shape"))
end
```

- `Area.area` is a generic function. Each `method` block is one method.
- `Circle` is more specific than `Any`, so a `Circle` argument dispatches to the first method; a `Rectangle` argument to the second; anything else to the third.
- Order of definition is irrelevant. If two signatures are incomparable and both applicable, that is an **ambiguity** and is reported (see §11).

### 1.2 `clause` — ordered pattern dispatch

```text
clause Sign.describe(value) when value < 0
    "negative"
end

clause Sign.describe(value) when value == 0
    "zero"
end

clause Sign.describe(value)
    "positive"
end
```

- `Sign.describe` is a function with three ordered clauses.
- The first clause whose pattern and guard match is executed.
- A clause with a bare parameter (no pattern, no guard) is a catch-all; if it is absent, the function is **partial** and calling it with unmatched input raises a runtime error (`FunctionClauseError`).
- A compile-time warning is emitted if no catch-all exists; `partial` on the head suppresses it (see §3).

### 1.3 Optional bodyless head

A bodyless declaration declares a generic function and carries modifiers:

```text
method Area.area(shape) closed          # head, closed to extension

method Area.area(shape: Circle)         # method body
    π * shape.radius ** 2
end
```

If no head appears, the first body establishes the function with default modifiers. A head is **required** if modifiers are needed; otherwise it may be omitted.

### 1.4 Examples of what each accepts

| Source | Construct | Hosted as |
|---|---|---|
| Julia `f(x::T) = …` | `method f(x: T)` | ✓ |
| Julia `f(x) = …` | `method f(x: Any)` | ✓ |
| Julia `f(x::T) where T <: Number = …` | `method f(x: T) where T <: Number` | ✓ |
| Julia `f(x::Vector{<:Real}) = …` | `method f(x: Vector{T}) where T <: Real` | ✓ |
| Julia `+(a::Pound, b::Pound) = …` | `method +(a: Pound, b: Pound)` | ✓ |
| Elixir `def f(0), do: …` | `clause f(0)` | ✓ |
| Elixir `def f(x) when is_integer(x), do: …` | `clause f(x) when is_integer(x)` | ✓ |
| Elixir `def f(%{k: v}), do: …` | `clause f(%{k: v})` | ✓ |
| Elixir `defimpl Size, for: BitString do … end` | `method Size.size(data: BitString)` | ✓ |

| Structural error | Reason |
|---|---|
| `clause f(x: Integer)` | clause parameters cannot have types |
| `method f(%{k: v})` | method parameters cannot be patterns |
| `method f(x: Integer) when x > 0` | methods cannot have guards |
| `clause f(x: T) where T <: S` | clauses cannot have `where` constraints |

### 1.5 Dispatch surface

`method` dispatches on **reified runtime types**. The dedicated runtime reifies:

- **primitive tags** — `Integer`, `Float`, `Bool`, `Atom`, `Symbol`, `BitString`, `Binary`, `Char`, `PID`, `Port`, `Reference`, `Function`, `Task`, `Channel`;
- **parametric types** — `List{Integer}` and `List{Float}` are distinct runtime types; `Map{K, V}`, `Tuple{...}`, and user parametric structs are likewise reified;
- **struct and record identity** — nominal, by type name;
- **union types and type aliases** — lowered to a disjunction of reified types;
- **`where` constraints** — a method's `where T <: S` participates in dispatch; `T` is bound to the reified type of the corresponding argument, and the constraint is checked against it.

Because types are reified, `method f(xs: List{Integer})` and `method f(xs: List{Float})` are **distinct methods**, and `method f(x: T) where T <: Real` and `method f(x: T) where T <: Atom` are **distinct methods**. Specificity resolution follows Julia's rules: a more specific method wins; incomparable applicable methods are an **ambiguity** and are reported.

The runtime is not assumed to specialize or JIT. Dispatch is by type identity and specificity, and the runtime may implement it as a table, a decision tree, or a cache. The language semantics do not depend on the implementation strategy.

### 1.6 Varargs and splatting

`x...` in a parameter list declares a variadic parameter; `f(xs...)` at a call site splats a sequence into positional arguments. Both constructs accept varargs.

---

## Part 2 — Type hierarchy

### 2.1 Root: `Any`

`Any` is the top of the type lattice. Every type is a subtype of `Any`. `Any` is the implicit type of an unannotated parameter in `method` and the implicit catch-all for `clause`.

### 2.2 The lattice

## Yes — still a DAG, with one precise qualification

The statement is true, but it should be stated more sharply because the corrected lattice differs from the original in ways that affect *why* it is a DAG.

### What makes it a DAG

There is exactly **one** multiple-inheritance point in the built-in lattice: `Bool` has two parents, `Integer` and `Atom`. That produces the diamond

```text
        Any
       /   \
   Real     Atom
     |        |
  Integer     |
       \     /
        Bool
```

`Bool` reaches `Any` by two paths — through `Integer → Real` and through `Atom`. Two paths to a common ancestor is the definition of a non-tree subgraph. So the hierarchy is a DAG, not a tree.

### What remains tree-like

Every other built-in type has a single parent. `Real`, `AbstractString`, `BitString`, `List`, `Array`, `Map`, `Dict`, `Tuple`, `Function`, `PID`, … all sit under `Any` on single paths. The only branching-with-reconvergence is the `Bool` diamond. This is why the original text could say "This is the one multiple-inheritance point in the lattice" and still call the hierarchy a lattice — it is a tree everywhere except that one node.

### Two qualifications that follow from the corrections

**1. `Number` does not create a second diamond, but it does make the lattice context-dependent.** When `Number` is introduced for a generic function that uses `Complex`, the subtype relation for that function is `Real <: Number <: Any` and `Complex <: Number <: Any`. That is a chain, not a diamond. But because `Number` is inserted *per function* and is absent elsewhere, there is **no single global lattice** — there is a family of lattices, one per activation context. Each member of the family is a DAG; the family as a whole is not one fixed graph. The `Bool` diamond is the only multiple-parent node in every member of the family.

**2. User extension keeps the DAG property if and only if it stays single-inheritance.** The current design permits `struct Foo <: Bar` — one parent. If user-defined multiple inheritance were ever allowed (`struct Foo <: Bar, Baz`), it would add diamonds at user control, and the language would have to specify conflict resolution for inherited clauses. As stated, user types are single-parent; the `Bool` diamond remains the only one.

### Combined statement (drop-in for the file)

> The type hierarchy is a **directed acyclic graph**, not a tree. The built-in lattice has exactly one multiple-inheritance point — `Bool` is a subtype of both `Integer` and `Atom` (§2.3) — which produces the single diamond in the graph. Every other built-in type has a single parent. Two further factors affect the graph's shape: (a) when `Number` is introduced as an optional extension point (§2.6), it is inserted above `Real` and `Complex` per generic function, so the lattice is *context-indexed* — a family of DAGs rather than one fixed DAG; and (b) user-declared types are single-inheritance (`struct Foo <: Bar`), so user extension cannot introduce new diamonds. Combinators (`Union`, `Vararg`, `Where`, `Type{T}`; §2.9) build structural types outside the nominal DAG and are resolved by the subtype algorithm rather than represented as nodes.

That paragraph is the correct generalisation of the original claim. The original sentence — "the type hierarchy is a directed acyclic graph, not a tree" — remains true verbatim, but for the corrected design it is more accurate to say *"a context-indexed family of DAGs, each with exactly one diamond."*

```text
## 2.2 Type lattice

### Legend

| Mark | Meaning |
|---|---|
| `(A)` | abstract — cannot be instantiated |
| `(C)` | concrete — has instances |
| `(S)` | singleton — exactly one instance |
| `+` | covariant — `T{Int} <: T{Any}` |
| `=` | invariant — `T{Int} ≮: T{Any}` |
| `⋯` | optional — present only when required |

### Lattice

```text
Any (A)                                                                [top]
│
├── Real (A)                                                           [numeric root]
│   ├── Integer (A)
│   │   ├── Signed (A)
│   │   │   → Int8 | Int16 | Int32 | Int64 | Int128 (C)
│   │   ├── Unsigned (A)
│   │   │   → UInt8 | UInt16 | UInt32 | UInt64 | UInt128 (C)
│   │   ├── BigInt (C)
│   │   └── Bool (C) ────────┐                                        [true | false]
│   ├── AbstractFloat (A)    │
│   │   → Float16 | Float32 | Float64 | BigFloat (C)
│   ├── Rational{T<:Integer} (C, =)
│   └── Irrational{Symbol} (A)
│       → Irrational{:π} | Irrational{:ℯ} | … (S)
│
│   ⋯ Number (A, optional) — inserted above Real and Complex
│       Complex{T<:Real} (C, =)
│       Real <: Number and Complex <: Number when Number is introduced
│
├── Atom (A)                 │
│   ├── Bool (C) ◄───────────┘                                        [diamond]
│   └── OtherAtom (C)                                                 [:nil | :ok | :error | :foo | …]
│
├── Symbol (C)                                                        [Julia :foo]
├── Char (C)
│
├── AbstractString (A)
│   ├── String (C)
│   ├── SubString{T<:AbstractString} (C, =)
│   └── SubstitutionString (C)
│
├── BitString (A)
│   └── Binary (C)                                                    [Elixir binary]
│
├── List{T} (C, +)                                                    [Elixir linked list]
├── Tuple{…} (C, + per slot)                                          [both]
├── Map{K, V} (C, +)                                                  [Elixir immutable map]
├── Dict{K, V} (C, =)                                                 [Julia mutable hash map]
├── Set{T} (C, =)
│
├── AbstractArray{T, N} (A, =)                                        [rank-N interface]
│   ├── Array{T, N} (C, =)                                            [dense tensor; concrete ∀ N ≥ 0]
│   │     Array{T, 0}              — 0-D tensor (scalar container)
│   │     Array{T, 1} ≡ Vector{T}  — 1-D tensor
│   │     Array{T, 2} ≡ Matrix{T}  — 2-D tensor
│   │     Array{T, 3}              — 3-tensor
│   │     Array{T, 4}              — 4-tensor
│   │     …
│   │     Array{T, N}              — N-tensor
│   ├── BitArray{N} (C, =)
│   ├── SparseMatrixCSC{T} (C, =)
│   └── SparseVector{T} (C, =)
│
├── Pair{A, B} (C, +)
├── NamedTuple{names, T} (C, +)
├── Ref{T} (C, =)
│
├── Nothing (S)                                                       [Julia nothing]
├── Missing (S)                                                       [Julia missing]
│
├── Function (A)
├── PID (C)
├── Port (C)
├── Reference (C)
├── Module (C)
└── Expr (C)
```

### Combinators — operations on types, not lattice nodes

```text
Union{A, B, …}       set-theoretic union
Vararg{T, N}         variable-arity tuple slot
Where{…}             universal quantification over type parameters
Type{T}              type of types; T is a type
T{<:S}               covariant slot in an invariant parameter
```

### Variance summary

| Type | Variance | Reason |
|---|---|---|
| `List{T}` | covariant `+` | Elixir lists are heterogeneous |
| `Tuple{…}` | covariant per slot `+` | Julia tuple covariance; Elixir heterogeneity |
| `Map{K, V}` | covariant `+` | Elixir maps are heterogeneous |
| `Array{T, N}` | invariant `=` | Julia arrays are invariant |
| `AbstractArray{T, N}` | invariant `=` | same |
| `Dict{K, V}` | invariant `=` | Julia dictionaries are invariant |
| `Set{T}` | invariant `=` | same |
| `Ref{T}` | invariant `=` | container identity matters |
| `SubString{T}` | invariant `=` | parametric on `AbstractString` |
| `Rational{T}` | invariant `=` | parametric on `Integer` |
| `Complex{T}` | invariant `=` | parametric on `Real` |
| `Pair{A, B}` | covariant `+` | per-slot covariance |
| `NamedTuple{names, T}` | covariant `+` | per-slot covariance |

### Type extension

The built-in lattice is a **starting set**, not a closed one. Users may declare `abstract type Foo <: Bar` and `struct Foo{T} <: Bar`; primitives via `primitive type Foo <: Bar`. New types appear under `Any` or beneath any existing node and inherit its variance rules.

---

## 2.3 `Bool` policy

`Bool` is a subtype of both `Integer` (Julia semantics) and `Atom` (Elixir semantics). This is the **one** multiple-inheritance point in the lattice.

- `true` and `false` are the only `Bool` instances.
- The surface tokens `:true` and `:false` are parse-time aliases for `true` and `false`; they denote `Bool` values, not `OtherAtom` values.
- `is_integer(true)` is `true` (because `Bool <: Integer`).
- `is_atom(true)` is `true` (because `Bool <: Atom`).
- `true + true == 2` (integer arithmetic on `Bool`).
- `true == :true` is `true` in the target language, because `:true` parses to the same `Bool` value.
- `true == Symbol(:true)` is `false` (Julia source; `Symbol` is a different type).

**Dispatch consequence.** If `method f(x: Integer)` and `method f(x: Atom)` are both applicable to `true`, neither is more specific (their parent types are incomparable), so `f(true)` is **ambiguous**.

- If `method f(x: Bool)` exists, it is more specific than both and wins.
- If only `f(Integer)` is defined, it wins unambiguously; likewise for `f(Atom)` alone.
- If both are defined and no `f(Bool)` exists, the ambiguity is reported per §11.1.

**Elixir-source rewrite.** Elixir's `is_integer(true)` returns `false`; the target's returns `true`. The transpiler rewrites Elixir's `is_integer/1` and `is_number/1` to `is_integer_excluding_bool/1` and `is_number_excluding_bool/1`, distinct built-ins that preserve Elixir semantics. Julia's `x isa Integer` / `x isa Number` are unchanged.

## 2.4 `Symbol` and `Atom`

`Atom` is **abstract**, with two concrete subtypes:

- `Bool` — instances `true`, `false` (§2.3).
- `OtherAtom` — every other atom, including `:nil`, `:ok`, `:error`, `:foo`, etc.

`Symbol` is a **separate type**, sibling to `Atom` under `Any`. Julia's `:foo` is a `Symbol`; Elixir's `:foo` is an `OtherAtom`.

- Julia's `:foo` transpiles to `Symbol(:foo)`.
- Elixir's `:foo` transpiles to `OtherAtom(:foo)`.
- The surface token `:foo` denotes `OtherAtom(:foo)` — **except** `:true` and `:false`, which are aliases for `true` and `false` (§2.3).
- The surface tokens `nil` and `:nil` both denote `OtherAtom(:nil)`.
- `Symbol(:foo)` is written explicitly; the bare token `:foo` is not a `Symbol`.
- `is_atom(x)` is `true` for `Bool` and `OtherAtom` values, `false` for `Symbol`.

## 2.5 `nothing`, `nil`, and `missing`

Three distinct values with three distinct types; the transpiler does not unify them.

- Julia's `nothing` → `Nothing` singleton.
- Elixir's `nil` → `OtherAtom(:nil)` (type `OtherAtom <: Atom`).
- Julia's `missing` → `Missing` singleton.

The equality `nil == :nil` is `true`; `nothing == nil` is `false`; `missing == nothing` is `false`.

## 2.6 Numeric tower

`Real` is the numeric root. `Number` exists **only as an optional extension point** above `Real` and `Complex`. If a generic function uses no `Complex` value, `Real` is sufficient. If it does, `Number` is introduced *for that function*, with `Real <: Number` and `Complex{T} <: Number`; `Complex` is **not** a subtype of `Real`.

Fixed-width subtypes:

- `Integer` (abstract) → `Signed` → `Int8` / `Int16` / `Int32` / `Int64` / `Int128`; `Unsigned` → `UInt8` / `UInt16` / `UInt32` / `UInt64` / `UInt128`; `BigInt`.
- `AbstractFloat` (abstract) → `Float16` / `Float32` / `Float64` / `BigFloat`.
- `Rational{T}` (parametric on `Integer`).
- `Irrational{Symbol}` (abstract; singleton subtypes `Irrational{:π}`, `Irrational{:ℯ}`, …).
- `Complex{T}` (parametric on `Real`; under `Number`, not under `Real`).

**Elixir mapping.** Elixir's `integer()` maps to `BigInt` by default; the transpiler may choose a sized integer when range analysis proves the value fits. Elixir's `float()` maps to `Float64`. The default-to-`BigInt` choice preserves overflow semantics at the cost of `Int64`-specialized dispatch not matching Elixir integer literals.

**Julia mapping.** Julia's integer literals are sized (`Int64` by default); `BigInt` is explicit. Julia's `Float64` literals map to `Float64`. A program that mixes both source languages and dispatches on `Int64` must declare its Elixir-sourced integers as `Int64` (or the transpiler must prove the range).

**`Bool` caveat.** Elixir's `is_integer/1` and `is_number/1` are rewritten to exclude `Bool` (see §2.3).

## 2.7 Structs, mutable structs, records

Three nominal aggregate constructs; each has a distinct runtime representation and distinct pattern-matching rules.

- **`struct`** — immutable, nominal. Field access `x.field`; pattern-matchable with `%Struct{field: var}`; constructible with defaults. Both Julia's `struct` and Elixir's `defstruct` map here.
  - **Not** a subtype of `Map{K, V}`. `Map.get/2`, `Map.keys/1`, `Map.put/3` are overloaded for `struct`, but structs do not flow into generic map code.
  - Elixir's `is_map(%User{})` is rewritten by the transpiler to `is_struct(%User{})`.
  - User-extensible: `struct Foo <: Bar` and `struct Foo{T} <: Bar` are legal.
- **`mutable struct`** — Julia only. Mutable, identity-based. Field access, but **no** field-destructuring patterns: identity, not content, is what a method dispatches on.
- **`record`** — Erlang only. Tagged tuple with named fields. Pattern-matchable with a distinct delimiter. Not a `struct`; `is_struct(record)` is `false`. OTP libraries use records directly, so `record` must be a first-class construct. `record` may not extend an abstract type — it is a tagged tuple, not a nominal subtype.

## 2.8 Abstract and primitive types

Julia's `abstract type T <: S end` declares an abstract nominal type; `primitive type T <: S` declares a primitive type. Both are hosted. Users can define new abstract types, new structs extending them, and new primitives. **The built-in lattice is a starting set, not a closed set.** Elixir has no user-defined abstract types; Elixir code that does not use them is unaffected.

## 2.9 Type combinators

Four operations build types from types. They are not lattice nodes.

- `Union{A, B, …}` — set-theoretic union. `Union{Int, Nothing}` matches `Int` or `Nothing`.
- `Vararg{T, N}` — variable-arity tuple slot. `Tuple{Int, Vararg{Int}}` matches `(Int,)`, `(Int, Int)`, `(Int, Int, Int)`, …
- `Where{…}` — universal quantification. `Vector{T} where T <: Integer`.
- `Type{T}` — the type of types. `Type{Int}` is the type of `Int`.

`Union` is essential for optionality (`Union{Int, Missing}`); `Vararg` for variadic tuples; `Where` for parametric constraints; `Type{T}` for reflection. These are operations on the lattice, not positions in it.

## 2.10 Tensors and `Array{T, N}`

`Array{T, N}` is the dense-array family, parameterized by element type `T` and rank `N`. It is **concrete for every rank `N ≥ 0`**, and `N` is a **type parameter, not a subtyping axis**:

- `Array{T, 0}` — 0-D tensor (scalar container).
- `Array{T, 1} ≡ Vector{T}`.
- `Array{T, 2} ≡ Matrix{T}`.
- `Array{T, 3}` — 3-tensor.
- `Array{T, N}` — N-tensor, any `N`.

`Vector{T}` and `Matrix{T}` are **aliases**, not subtypes, of `Array{T, N}`. `Array{Int, 1}` and `Array{Int, 2}` are unrelated; only `AbstractArray{T, N}` unifies them.

`Array{T, N}` is invariant in both `T` and `N`: `Vector{Int}` is not a subtype of `Vector{Any}`. Covariance is written `AbstractArray{<:Integer, 1}`.

**BEAM representation.** BEAM has no native contiguous array. The transpiler must choose and document a representation for every `Array{T, N}`:

- **Flat `Binary`** — `{rank: N, shape: Tuple{Int, …}, data: Binary}` with packed elements. Fast for numeric types (`<<x:64/float>>` for `Float64`); each element access is a binary operation.
- **Nested `Tuple`/`List`** — correct for `N ≥ 1`; each element is boxed; O(N) hops per access.
- **`:array` module backing** — Erlang's functional array; arbitrary rank; efficient updates; not contiguous.

The choice is per `method` specialization or per element type `T`. It must be documented in the prelude. Without it, `Array{Float64, 3}` is emitted as a nested list and every tensor operation is slower than it needs to be.

---

## Part 3 — Modifiers

A modifier appears **only on a bodyless head**, never on a body clause. This keeps the head's job and the clause's job strictly separate: the head declares the model and policy; the clause declares the match.

| Modifier | Applies to | Effect |
|---|---|---|
| `open` | `method` | external modules may add methods (default for `method`) |
| `closed` | `method` | seals against external extension |
| `private` | `method`, `clause` | not exported; unconditionally closed |
| `partial` | `clause` | suppresses exhaustiveness warning |
| `group Name` | `method` | declares the function as a member of a group (see §4) |

`clause` heads are always closed — `open` on a `clause` head is a compile error. Ordered dispatch has no well-defined cross-module ordering (load order, dependency order, hot reload), so closed is the only coherent choice.

---

## Part 4 — Groups: protocols and OTP behaviours

A **group** is a set of `method` heads that must be implemented together as a unit. Groups serve two purposes, both fundamentally the same shape:

- **Elixir protocols** (`defprotocol` / `defimpl`) — a set of functions, one implementation per first-argument type.
- **Erlang/OTP behaviours** (`gen_server`, `supervisor`, `application`) — a set of callbacks, one implementation per module.

```text
method Size.size(data)   group Size
method Size.empty?(data) group Size
```

Every group function is an ordinary `method`. Implementation for `BitString`:

```text
method Size.size(data: BitString)
    byte_size(data)
end

method Size.empty?(data: BitString)
    byte_size(data) == 0
end
```

Implementation for `Map`:

```text
method Size.size(data: Map)
    map_size(data)
end

method Size.empty?(data: Map)
    map_size(data) == 0
end
```

Fallback (corresponds to `@fallback_to_any true`):

```text
method Size.size(data: Any)
    :unknown
end

method Size.empty?(data: Any)
    :unknown
end
```

### 4.1 Group unit and exhaustiveness

The **unit** of a group implementation is the dispatch key:

- for a **protocol-style** group, the first-argument type (`BitString`, `Map`, `Any`);
- for a **behaviour-style** group, the module prefix of the function name (`MyServer`).

A unit is complete when every group member has a method applicable to that key. Exhaustiveness is checked **per unit at consolidation**. A unit that is incomplete at consolidation is a build error naming the group, the key, and the missing member. A unit introduced or extended by hot loading is checked at the first call to a missing member, which raises `GroupMemberMissing` with the same three facts.

### 4.2 Group rules

- Group members are all `method`. `clause` heads cannot carry `group`.
- Group openness follows the head: `open` by default (matching `defimpl`'s open extension), `closed` seals the group.
- **Fallback via `Any`**: the `Any` unit is the fallback. No separate keyword is needed.
- **Multi-argument group functions are allowed** but introduce Julia's ambiguity problem inside a protocol namespace. Group members that dispatch on more than the first argument must be documented as stronger than the corresponding Elixir protocol; ambiguity between incomparable signatures is reported per §11. Elixir's first-argument-only dispatch is preserved as the default because that is what the source code expects.

### 4.3 OTP behaviours are groups

A `gen_server` implementation in the target language is a group:

```text
method MyServer.init(args)                     group GenServer
method MyServer.handle_call(msg, from, state)  group GenServer
method MyServer.handle_cast(msg, state)        group GenServer
method MyServer.handle_info(msg, state)        group GenServer
method MyServer.terminate(reason, state)       group GenServer
method MyServer.code_change(old, state, extra) group GenServer
```

The unit is `MyServer`. The compiler checks at consolidation that every required callback for `MyServer` is implemented together. The actual server loop is the runtime's `gen_server` implementation, called through the runtime's BIFs. The language does not reimplement it.

---

## Part 5 — Transpilation of Elixir protocols

Given:

```elixir
defprotocol Size do
  def size(data)
  def empty?(data)
end

defimpl Size, for: BitString do
  def size(data), do: byte_size(data)
  def empty?(data), do: byte_size(data) == 0
end

defimpl Size, for: Map do
  def size(data), do: map_size(data)
  def empty?(data), do: map_size(data) == 0
end

@fallback_to_any true
defimpl Size, for: Any do
  def size(_data), do: :unknown
  def empty?(_data), do: :unknown
end
```

Target:

```text
method Size.size(data)   group Size
method Size.empty?(data) group Size

method Size.size(data: BitString)
    byte_size(data)
end

method Size.empty?(data: BitString)
    byte_size(data) == 0
end

method Size.size(data: Map)
    map_size(data)
end

method Size.empty?(data: Map)
    map_size(data) == 0
end

method Size.size(data: Any)
    :unknown
end

method Size.empty?(data: Any)
    :unknown
end
```

The `defprotocol` becomes a group of `method` heads. Each `defimpl` becomes a set of typed `method` clauses. The `Any` fallback becomes an `Any` clause. Nothing on a clause carries protocol-specific syntax. The unit for `BitString` is complete at consolidation; if `Size.empty?(BitString)` were absent, consolidation would report `Size`, `BitString`, `Size.empty?`.

---

## Part 6 — Combined features (lifted, not hybridized)

These features have **identical or strictly-complementary semantics** in both source languages. The target language picks the clearer surface and lifts it to the whole language. Each lift is strictly additive to the source language that lacks it — not a hybrid, and not a compromise.

### 6.1 Features both languages share — one surface

- **Pipe `|>`** — the host's pipe. Julia's multi-argument `x |> f(y)` (which means `f(y)(x)`) is rewritten at translation time to `f(y)(x)` or `f(y, x)` as the source intended; Elixir's `x |> f(y)` (which means `f(x, y)`) is the host's form.
- **Anonymous functions `x -> expr`** — the host's form; shorter than Elixir's `fn x -> ... end`. Multi-clause anonymous functions lower to `case` inside a `do` block.
- **String interpolation `"#{expr}"`** — the host's form. A literal `#{` is escaped `\#{`.
- **Tuples and multiple assignment `{a, b} = pair`** — identical in both.
- **Comprehensions `[expr for x <- xs, filter]`** — Elixir's form is the superset (patterns, filters, `into:`); adopted whole.
- **Splat `xs...`** — shared; call-site and parameter position.
- **`==`, `!=`, `===`, `!==`** — strict equality, total on all terms, identical in both.
- **`in`, `not in`** — membership, identical in both.

### 6.2 Elixir features lifted into Julia-transpiled code

- **`do … end` blocks for higher-order calls.**
  ```text
  Enum.map(xs) do x ->
      y = x * 2
      y + 1
  end
  ```
- **`cond do … end`** — first-true-wins.
- **Functional update `value | field: new`.**
  ```text
  updated = alice | age: 31
  cfg     = config | timeout: 10_000
  ```
- **Pattern matching on typed structs — `%Struct{field: var}`.**
  ```text
  clause greet(%User{name: n, age: a}) when a >= 18
      "Hello, #{n}"
  end
  ```
- **Exhaustiveness across `clause`, as far as decidable.** A clause set is exhaustive iff it contains an irrefutable, guard-free clause. Guards defeat the check; `partial` suppresses the warning. This is a warning, not a hard error, because guards make exhaustiveness undecidable in general.
- **`with` — pattern-match chaining with short-circuiting.**
  ```text
  with {:ok, user}    <- fetch_user(id),
       {:ok, account} <- fetch_account(user),
       true           <- is_active?(account) do
      {:ok, account}
  else
      {:error, reason} -> {:error, reason}
      false            -> {:error, :inactive}
  end
  ```
- **`case` — expression-level pattern matching.**
  ```text
  case x do
      0            -> :zero
      n when n > 0 -> :positive
      _            -> :negative
  end
  ```
- **`try` / `rescue` / `catch` / `after`.**
  ```text
  try do
      risky()
  rescue
      e -> handle(e)
  catch
      :thrown -> recover()
  after
      cleanup()
  end
  ```

### 6.3 Julia features lifted into Elixir-transpiled code

- **Keyword arguments with types and defaults.** `;` separates positional parameters from keyword parameters; a call supplies keywords by name.
  ```text
  method connect(host: String; port: Integer = 5432, timeout: Integer = 5_000)
      open_connection(host, port, timeout)
  end

  connect("db"; port: 5433)
  ```
- **`where T <: S` constraints** — participate in dispatch (§1.5). Elixir's `@spec` is redundant and is not hosted; the signature is the specification.
  ```text
  method sum(values: List{T}) where T <: Real
      reduce(values, 0.0, +)
  end
  ```
- **Parametric types in signatures** — `List{T}`, `Map{K, V}`, `Tuple{...}`, and user parametric structs are reified and participate in dispatch.
- **Abstract and primitive types** — `abstract type`, `primitive type`.
- **Type aliases** — `alias NewName = OldType` or `const NewName = OldType`.
- **Multiple assignment from tuples and splats** — as in both.

### 6.4 Operator overloading

Operators are `method`s, as in Julia. The language has built-in methods for `+`, `-`, `*`, `/`, `div`, `rem`, `**`, `<`, `>`, `<=`, `>=`, `==`, and the rest on the primitive numeric types, and user code may add methods:

```text
method +(a: Pound, b: Pound)
    Pound(a.cents + b.cents)
end
```

Elixir does not overload operators; Elixir code that uses the built-in operators is unaffected, and Elixir code that does not define operator methods sees only the built-in ones.

`^` is the pattern pin operator, not exponentiation; exponentiation is `**`.

### 6.5 Comparison — two constructs

Julia and Elixir disagree on comparison of unrelated types. The language provides **two visibly distinct constructs**:

- **`<`, `>`, `<=`, `>=`** — Elixir's **total order** across all terms (`number < atom < reference < function < port < pid < tuple < map < list < bitstring`). Never raises; always returns a boolean. This is the form used in `clause` guards.
- **`typed_lt`, `typed_gt`, `typed_le`, `typed_ge`** — Julia's **typed comparison**. Raises `MethodError` when the operands are of unrelated types. This is the form Julia's `<`, `>`, `<=`, `>=` lower to.

The token tells the reader which semantics apply. There is no context-dependent comparison.

`==`, `!=`, `===`, `!==` are total in both source languages and are one construct.

### 6.6 Broadcasting

Julia's element-wise broadcasting is hosted with its own syntax, distinct from the scalar operators: `.+`, `.-`, `.*`, `./`, `.^`, and so on, plus `f.(xs)` for broadcasting a function. The dot is part of the operator token, so the reader knows the operation is element-wise. Elixir has no broadcasting; Elixir code that does not use dotted operators is unaffected.

### 6.7 Macros

Julia's AST macros and Elixir's `quote`/`unquote` macros are both hosted, with **explicit markers** so the reader knows which macro system applies:

```text
macro julia @my_macro x
    ...
end

macro elixir my_macro(x) do
    quote do ... end
end
```

A macro invocation carries the same marker: `@my_macro(x)` (Julia) or `my_macro(x)` (Elixir). The marker is part of the token, so the reader knows which expansion rules apply. The two macro systems do not unify; they coexist as distinct constructs.

### 6.8 Error channels — separate

Both source languages have error handling, but the channels differ. The language keeps them separate:

- **`raise` / `error`** — exceptions. Caught with `rescue`. For unexpected failures. Julia's `error(...)` and `throw(...)` both lower here; Julia's `catch` catches both.
- **`throw`** — non-local control flow. Caught with `catch`. For expected early exits.
- **`exit`** — process termination. Does not unwind the stack; sends an exit signal to links and monitors. Lowering `exit` to `raise` breaks supervision.

Julia's `try`/`catch` lowers to `try`/`rescue`/`catch` (it catches both `raise` and `throw`, but not `exit`, matching Julia's semantics). Tagged-tuple error handling (`{:ok, value}` / `{:error, reason}`) is an Elixir idiom, preserved as ordinary tuples.

### 6.9 Mutability — separate

- `struct` is immutable (Elixir semantics; Julia's `struct`).
- `mutable struct` is mutable (Julia semantics). It does not support field-destructuring patterns.
- `record` is an Erlang tagged tuple, distinct from both.

### 6.10 Modules

```text
module MyApp.Users
    ...
end
```

The language hosts both module systems:

- **Elixir's** `alias`, `import`, `require`, `use`, and `defmodule`-style module bodies.
- **Julia's** `using`, `import`, `export`, `module`, and `baremodule`.

`use` (Elixir) and `using` (Julia) are **distinct tokens** with distinct semantics; the reader knows which applies. `import` exists in both with different meanings (Elixir: bring names into scope, optionally qualified; Julia: bring names into scope with qualification rules); the language provides `import` (Elixir) and `import` (Julia) as distinct forms distinguished by their argument syntax, or as `import` and `using` respectively. The transpiler disambiguates from the source.

### 6.11 Literals

- `[a, b, c]` — a `List`.
- `{a, b}` — a `Tuple`.
- `%{k: v}` — a `Map`.
- `%Struct{field: value}` — a struct.
- `<<...>>` — a `BitString` (§7.4).
- `:atom` — an `Atom` (Elixir); `Symbol(:foo)` — a `Symbol` (Julia).
- `"string"`, `'charlist'`, `'c'` (Char, Julia), `123`, `1.5`, `nothing`, `nil`, `missing` — as in the source.

---

## Part 7 — Language-level primitives

The language exposes the primitives that both source languages need. The dedicated runtime must provide them; the language semantics are defined in terms of the primitives, not in terms of any particular runtime implementation.

### 7.1 Processes and messages (Elixir)

- **`spawn`**, **`spawn_link`**, **`spawn_monitor`** — create a process; return a PID (or `{PID, Ref}` for `spawn_monitor`).
- **`!`** — asynchronous send. The syntax `pid ! message` is a first-class operator.
- **`receive`** — a block of `clause`-shaped arms that consume messages from the mailbox, with an optional `after` timeout.
  ```text
  receive do
      {:msg, body}         -> handle(body)
      {:EXIT, pid, reason} -> restart(pid, reason)
      _                    -> :ignore
  after
      5_000 -> :timeout
  end
  ```
  This reuses the `clause` construct: patterns, guards, first-match-wins, plus `after`.
- **`link`**, **`monitor`**, **`exit`** — first-class bindings. Supervisors are ordinary processes that use them.

### 7.2 Tasks and channels (Julia)

- **`@spawn expr`** — start a task.
- **`Task`** — a task handle; `fetch(t)`, `wait(t)`, `schedule(t)`.
- **`Channel{T}(n)`** — a typed channel; `put!(ch, v)`, `take!(ch)`, `isready(ch)`.
- **`@async`**, **`@sync`** — structured concurrency macros (hosted as Julia macros, §6.7).

### 7.3 OTP behaviours

`gen_server`, `supervisor`, and `application` are `group` declarations whose callbacks are `method` heads (§4.3). The server loop is the runtime's library code. Restart strategies and child specs are ordinary terms (tuples and maps), not special syntax.

### 7.4 Records

Erlang records are compile-time tagged tuples with named fields. They are **distinct from `struct`**, which is a map. A `record` construct compiles to a tagged tuple and is pattern-matchable with named-field syntax using a distinct delimiter (see §11.2). A `struct` compiles to a tagged map and is matched with `%Struct{...}`. They do not unify because they match differently at the runtime level, and OTP libraries use records directly.

### 7.5 Binaries and bit syntax

Erlang's bit syntax is a language construct, not a library:

```text
<<size:16, data/binary>>                    # construction
```

```text
receive do
    <<msg_type:8, payload/binary>> -> handle(msg_type, payload)
end
```

Segment size, type, and unit modifiers are supported. This is used pervasively for network protocols, file formats, and socket payloads.

### 7.6 Runtime requirements

The dedicated runtime must provide: process creation and message passing, task scheduling, typed channels, records as tagged tuples, bit syntax, and reified runtime types (including parametric types) for `method` dispatch. It must not be assumed to specialize or JIT; dispatch is by type identity and specificity.

---

## Part 8 — Worked example

The following module combines every feature discussed. It reads naturally to both Julia and Elixir programmers, and every construct does the job its source language made it good at.

```text
struct User
    name:    String
    age:     Integer = 0
    address: String  = ""
end

method greet(user: User; greeting: String = "Hello")
    "#{greeting}, #{user.name}"
end

clause classify(%User{age: a}) when a < 13
    :child
end

clause classify(%User{age: a}) when a < 20
    :teen
end

clause classify(%User{age: a}) when a < 65
    :adult
end

clause classify(%User{})
    :senior
end

method promote(user: User) closed

method promote(user: User)
    user | age: user.age + 1
end

method notify_all(users: List{User}, template: String)
    users
    |> Enum.filter(u -> classify(u) == :adult)
    |> Enum.map() do u ->
        message = template |> String.replace("{{name}}", u.name)
        Notification.send(u, message)
        promote(u)
    end
end

method config_defaults() private

method config_defaults()
    %{
        timeout:   5_000,
        retries:   3,
        log_level: :info
    }
end

method connect(host: String; port: Integer = 5432, timeout: Integer = 5_000)
    cfg = config_defaults()
    open(host, port, cfg | timeout: timeout)
end
```

Reading the example top to bottom:

- `struct` with defaults — Elixir ergonomics, Julia's nominal typing, map-like for Elixir interop.
- `method` — Julia's multiple dispatch and keyword arguments (`;` separates positionals from keywords).
- `clause` + `%User{age: a}` patterns — Elixir pattern matching on a nominal struct.
- `when a < 13` — Elixir's total-order comparison inside a guard.
- `user | age: ...` — Elixir's functional update on an immutable struct.
- `|>` pipeline — the host's pipe.
- `Enum.map() do u -> ... end` — Elixir's block syntax.
- `private`, `closed` — modifiers on bodyless heads, never on bodies.
- `"{{name}}"` — a literal placeholder; `#{}` interpolation is not triggered.
- `List{User}` — a reified parametric type; the method dispatches on `List{User}` specifically.
- `Number`/`Real` — the full numeric tower is available; `π` is a `Float64` constant, and Julia numeric code that uses `Rational`, `BigFloat`, or `Complex` is hosted.

---

## Part 9 — Illegal and structurally invalid constructs

Every construct below is a compile-time error. These rules are what keep the two dispatch models from leaking into each other. They are **structural** errors; they do not reject source constructs, because every source construct is hosted by a distinct token.

```text
# ERROR: clause heads cannot be extended from another module
module X
    clause Sign.describe(value) when is_custom(value)
        "custom"
    end
end

# ERROR: clause parameters cannot have type annotations
clause Sign.describe(value: CustomType)
    "custom"
end

# ERROR: method parameters cannot be patterns
method Area.area(shape: %{radius: r})
    π * r ** 2
end

# ERROR: method clauses cannot have guards
method Area.area(shape: Circle) when shape.radius > 0
    π * shape.radius ** 2
end

# ERROR: clause heads cannot carry group
clause Size.volume(data) group Size

# ERROR: private heads cannot be extended from another module
module Y
    method Normalize.magnitude(vector: List)
        0
    end
end

# ERROR: closed heads cannot be extended
module Z
    method Money.add(a: Pound, b: Pound)
        Pound(a.cents + b.cents)
    end
end

# ERROR: ordered clause heads cannot be opened
clause Sign.describe(value) open
    "anything"
end

# ERROR: modifiers appear only on bodyless heads
method promote(user: User) closed
    user | age: user.age + 1
end

# ERROR: macro markers are required
@my_macro x                       # ambiguous: Julia or Elixir macro?
```

---

## Part 10 — Decisions and rules (compact reference)

| Topic | Decision |
|---|---|
| Dispatch | `method` (typed, unordered, specificity-resolved, open) and `clause` (pattern, ordered, first-match, closed) are separate keywords |
| Discriminator | the keyword itself; no modifier can flip a construct |
| Head vs. clause | head carries modifiers, clause carries matches; modifiers never on bodies |
| Dispatch surface | reified runtime types, including parametric types, unions, and `where` constraints |
| Numeric tower | fully hosted: `Number`, `Real`, `Integer`, `AbstractFloat`, `Rational`, `Irrational`, `Complex`, `BigInt`, `BigFloat`, fixed-width types |
| `Bool` | subtype of both `Integer` (Julia) and `Atom` (Elixir); `true`/`false` are `Bool` values and the atoms `:true`/`:false` |
| `Symbol` vs. `Atom` | distinct types; Julia's `:foo` is `Symbol(:foo)`, Elixir's `:foo` is `Atom(:foo)` |
| `nothing`, `nil`, `missing` | three distinct values of three distinct types (`Nothing`, `:nil`, `Missing`) |
| Operator overloading | hosted as `method` on the operator name; built-in methods for primitives |
| Comparison | `<`, `>`, `<=`, `>=` are total order (Elixir); `typed_lt`, `typed_gt`, `typed_le`, `typed_ge` are typed (Julia) |
| Equality | `==`, `!=`, `===`, `!==` are total and shared |
| Broadcasting | distinct dotted operators `.+`, `.*`, `f.(xs)` — Julia only |
| Macros | hosted with `macro julia` / `macro elixir` markers; invocations carry the same marker |
| Protocol | a `group` of `method` heads; `Any` unit is the fallback |
| OTP behaviour | a `group` of `method` heads; callbacks are `method` signatures; unit is the module prefix |
| Group unit | dispatch key (first-arg type for protocols, module prefix for behaviours); exhaustiveness checked per unit at consolidation |
| `private` | unconditionally closed, not exported, cannot be extended |
| `partial` | suppresses exhaustiveness warning on `clause` head |
| Exceptions / throws / exits | `raise` / `rescue`, `throw` / `catch`, `exit` — three distinct channels; Julia `try`/`catch` catches `raise` and `throw` |
| `struct` / `mutable struct` / `record` | immutable and map-like and pattern-matchable / mutable and identity-based / tagged tuple |
| Modules | both `using`/`import`/`export`/`module` (Julia) and `alias`/`import`/`require`/`use`/`defmodule` (Elixir), distinguished by token |
| Keyword arguments | `;` separates positional from keyword; keywords supplied by name |
| Varargs | `x...` in parameters; `f(xs...)` at call sites |
| Concurrency | processes/messages (Elixir) and tasks/channels (Julia), distinct constructs |
| Ambiguity | warning at method addition; hard error at consolidation |
| Consolidation | build-time default, development mode available |
| Runtime | dedicated; must reify types (including parametric) and provide processes, tasks, channels, records, bit syntax |
| Coverage | 100% of both source ecosystems; every construct is hosted by a distinct token |
| Rejection | only structural errors (mixing `method` and `clause` grammars, modifiers on bodies, unmarked macros); no source construct is rejected |

---

## Part 11 — The polymorphism space

| Topic | Decision |
|---|---|
| Polymorphism model | Four source mechanisms mapped onto two constructs; the three-axis taxonomy documents the mapping and configures nothing |
| Group dispatch | `group` enforces implementation completeness per unit; it does not dispatch. Dispatch remains `method`'s specificity resolution |
| Group unit | Module prefix (behaviour-style) or first-arg type (protocol-style); read from the head, never configured |
| Abstract type + fallback | Hosted as `abstract type` + `method`; hierarchy dispatch is specificity resolution |
| Holy trait | Hosted as `method` on `Type{T}`; predicate dispatch is specificity resolution |
| Cross-ecosystem interop | `method` over reified types (§1.5); no unified contract construct |
| Configuration as discriminator | Not permitted; a construct whose semantics depend on an axis setting fails the single test, same as a single `def` with `dispatch:` flags |
| New polymorphism points | Get a new keyword, never axes on an existing keyword |

## Part 12 — Open questions (implementation phase)

These decisions are not settled by the design, only by the compiler implementation. Each is localized to a single module or tool and does not affect the surface grammar.

1. **Consolidation timing.** Build-step, load-step, or runtime, for `group` dispatch tables. Affects when exhaustiveness and ambiguity errors surface.
2. **Record delimiter.** A distinct `record` pattern delimiter (so `%Struct{...}` and record patterns are visibly different), or a tagged-tuple special case with named-field sugar. Either works; the choice affects the pattern-matching grammar, and the single test requires the delimiters to differ.
3. **Macro marker syntax.** `macro julia` / `macro elixir` and the corresponding invocation markers; the exact tokens are an implementation detail as long as the marker is present on every macro definition and invocation.
4. **Module-system disambiguation.** The exact surface distinction between Elixir's `import` and Julia's `import`/`using`; the transpiler must disambiguate from the source, and the surface must make the distinction visible to the reader.
5. **Runtime reification strategy.** How parametric types are represented and how `where` constraints are checked at dispatch time. This is a runtime implementation question; the language semantics require only that reification and dispatch behave as specified in §1.5.

---

## Part 13 — The single test

Any future proposal can be evaluated against one question:

> **If a programmer cannot tell from the keyword or operator alone which source language's semantics apply, the construct is not clean enough.**

That test produced the split of `method` from `clause`, the separation of error channels, mutability, comparison, macro systems, and concurrency, the distinction between `struct` and `record`, the distinction between `Symbol` and `Atom`, and the distinction between `nothing`, `nil`, and `missing`. It also produced the pairing of `<` (total order) with `typed_lt` (typed comparison), and the pairing of `@my_macro` (Julia) with `my_macro` (Elixir).

Applied to any future feature, it will produce the right answer again — for a language that hosts **every** construct of both source languages, with a distinct token for every distinct semantics.
