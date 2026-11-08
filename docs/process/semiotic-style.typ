= Semiotic style

#strong[Status: visual/documentation convention.] Technical names remain
normative. Human names are aliases for reading; colors identify the kind of
material, not a second taxonomy.

The palette is Catppuccin Mocha.

#table(
  columns: (auto, auto, auto, auto, 1fr),
  align: (left, left, left, left, left),
  table.header([Technical], [Human], [Token], [Hex], [Kind]),
  [Action], [#text(fill: rgb("#f5c2e7"))[Arthur]], [pink], [`#f5c2e7`], [Specification],
  [Question], [#text(fill: rgb("#cba6f7"))[Lia]], [mauve], [`#cba6f7`], [Specification],
  [Action + Question], [#text(fill: rgb("#f5c2e7"))[Arthur] #text(fill: rgb("#cba6f7"))[and Lia]], [pink/mauve], [both], [Combined specification],
  [Unification], [Unification], [yellow], [`#f9e2af`], [Implementation],
)

Rules:

- Use #text(fill: rgb("#f5c2e7"))[pink] for Action specifications.
- Use #text(fill: rgb("#cba6f7"))[mauve] for Question specifications.
- Use #text(fill: rgb("#f9e2af"))[yellow] for implementation notes and file
  anchors.
- Do not replace the technical name in normative prose. Write
  #text(fill: rgb("#f5c2e7"))[Arthur] (Action) or
  #text(fill: rgb("#cba6f7"))[Lia] (Question).
- Implementation maps use the yellow label, for example
  #text(fill: rgb("#f9e2af"))[Unification].

The names are provisional reading aids: Arthur is the Action specification,
Lia is the Question specification, and Arthur and Lia together are the Scale
programming language.
