# book-host

Books and spells (D184). A `.book` file is a package of `logic` definitions.
The file stem is the package name, `1` is the version, and the SHA-256 of the
file is the source hash. A book with an exported `main` is a spell; without
`main` it is a library. The contract is `docs/language/action/books-and-spells.typ`.

## Marshalling

- A prose `cast` lowers to `$book.cast#<arity>`. The adapter pops the path, the
  word, and the argument values from the caller's stack; all are store entries.
- The path and word must be text. The remaining values are passed to the book
  word as its arguments; a number or a text value is already a store entry, so
  no conversion is needed.
- The book runs in a fresh frame with the caller's store, vocabulary, tables,
  host-word adapters, and base directory. Definitions are bound by a `Let`
  chain; a function is called through `CallWord` + `Apply`, and a value
  definition returns its bound value.
- The result is pushed onto the caller's remaining stack. The book's last
  command status is carried back when it set one.
- A relative path resolves against the calling `.prose` file's directory, or
  the book's directory for a nested cast. A missing file, an empty book, a free
  sentence, an unknown word, or a non-text path or word is a named refusal.

## CLI

`BookRunner.load` and `BookRunner.runWord` are the component API; the base adds
`--book` and `--cast`. Stage A has a zero-argument `main` and no book imports.
