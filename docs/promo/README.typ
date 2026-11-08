= PR material

#strong[Status: first promotional material.] This directory holds public-facing
material. The normative language documentation remains in `docs/`.

== Lia

`docs/promo/lia.typ` is the first children's introduction. Lia is a small
yellow animal who asks questions and finds pieces that fit. She is the
#text(fill: rgb("#b8860b"))[Unification] mascot: “When two ideas fit, I say:
snap!”

The page uses a simple puzzle card, three ideas (ask, match, smile), and
large shapes rather than a technical definition. Print or render it as a
one-page PDF for a child.

== Training-free language strategy

Scale's public strategy is to narrow, not retrain:

- existing language models already contain a broad superset of English;
- Prose is intentionally a small, controlled subset of that surface;
- Scale defines semantics, pragmatics, and syntax that the model can follow
  from examples and constraints;
- no model retraining is required merely to introduce the language;
- this is a lowering and prompting strategy, not a claim that a model knows
  Scale without its documentation.

The model must still receive the Scale contract, examples, refusals, and
current state. Training-free means “no new training pass is required,” not
“no context or verification is required.”

== Material set

- `docs/promo/lia.typ` — “Meet Lia” children's poster.
- `docs/language/semiotics.typ` — Arthur/Lia/Scale layer names.
- `docs/process/semiotic-style.typ` — Catppuccin color and naming rules.
- `docs/language/action/README.typ` — Arthur, the Action language.
- `docs/language/question/README.typ` — Lia, the Question language.
