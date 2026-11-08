= Real numbers

#strong[Status: implemented.] This is the feature contract for the Prose
real-literal slice of the Prelude port. It adds one literal form for base-10
numbers with an optional fractional part; it does not add a second numeric type
or a second arithmetic.

== Goal and boundary

The Prelude's arithmetic and benchmarks use floating point. Prose previously
accepted optionally signed integers only, so a fractional constant could not be
written. This slice widens the existing number token to accept a fractional
part. The value type does not change: every number is an IEEE-754 binary64
value, and the existing arithmetic words (`add`, `subtract`, `multiply`,
`divide`) already operate on it.

Port targets covered: arithmetic, comparisons, benchmarks, and any Prelude
constant that is not integral. The slice is lexical and syntactic only; no word,
value, or kernel representation changes.

== Decided

+ #strong[One form covers integers and reals.] An optionally signed base-10
  integer, optionally followed by a period and one or more digits: `42`, `-1`,
  `3.5`, `-0.25`. There is no separate integer literal and real literal, and no
  separate integer and real arithmetic.
+ #strong[A period belongs to the number only when a digit follows it.] In
  `logic main = 1.5.` the first period is the fractional point and the second
  is the sentence full stop. In `logic main = 1.` the period is the full stop,
  so the value is the integer `1`. This rule keeps the sentence terminator
  unambiguous without a second literal marker.
+ #strong[Invariant parsing to binary64.] The digits are parsed with the
  invariant culture into a 64-bit float. Integers stay exact up to `2^53`;
  fractional values are the nearest binary64 value, so `0.1` is not exact.
+ #strong[Same token in every expression position.] A number is an expression,
  a pipeline stage argument, a call argument, and a pattern. A number pattern
  matches the same binary64 value; there is no epsilon comparison.
+ #strong[No new refusal case.] A malformed number is a token or parse error
  that already exists (`UnexpectedToken`, `ExpectedExpression`,
  `ExpectedPeriod`); no numeric-specific `ProseError` case is added.

== The literal form

#table(
  columns: (auto, 2fr),
  align: (left, left),
  table.header(table.cell(fill: luma(240))[Form], table.cell(fill: luma(240))[Meaning]),
  table.hline(),
  [`42`], [the binary64 value 42],
  [`-1`], [the binary64 value -1],
  [`3.5`], [the binary64 value 3.5],
  [`-0.25`], [the binary64 value -0.25],
  [`1.`], [the integer 1 followed by the sentence period],
  [`1.5.`], [the value 1.5 followed by the sentence period],
  [`.5`], [refused: a sentence cannot begin with a period],
  [`1e3`], [not one token: the number 1 followed by the word `e3`],
)

A number may appear wherever an expression or a pattern may appear:

```text
logic main = add(1.5 2.5).
logic scale x = multiply(x 0.5).
logic sign x = match x, when 0 then "zero", otherwise "nonzero" close.
```

== Limits and non-goals

+ No exponent form (`1e3`), no leading-dot form (`.5`), no underscores, no
  radix prefixes, and no locale-specific separators.
+ No exact decimal or rational type; every number is binary64.
+ No epsilon or approximate pattern match; equality is exact binary64
  equality.
+ No bignum, `NaN`, or `Infinity` literal; those arrive through host words if a
  port needs them.
+ No numeric formatting surface; converting a number to text stays a host word.
+ No separate integer and real arithmetic; the existing words are the whole
  arithmetic.

== Tests derived from this contract

+ A fractional literal parses to `NumberLiteral 3.5`; a negative fractional
  literal parses to `NumberLiteral -0.25`.
+ An integer followed by the sentence period still parses as an integer, so the
  period is not swallowed into the number.
+ Real arithmetic through the existing words: `add(1.5 2.5)` evaluates to 4.
+ A real pattern matches its value: `match 1.5, when 1.5 then 1, otherwise 0
  close` returns 1.
+ A leading-dot form is refused with `ExpectedExpression`.
+ A generated property: for arbitrary dyadic decimals (multiples of `1/8`),
  the parsed literal equals the exact binary64 value (property).

== Acceptance for the port

The slice is ready when a Prelude arithmetic or benchmark constant can be
written as a fractional literal and the existing behavioral tests pass without
host-side special cases.
