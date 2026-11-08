# shell-host

The shell effect boundary (D183). This component owns every OS process
operation for the non-interactive prose shell. The kernel and the Prelude hold
no process code: the Prelude offers an unhandled token to an attached
`HostWordAdapter`, and this component implements that adapter on top of
`System.Diagnostics.Process`.

## Marshalling

- A shell call lowers to the token `$shell.run#<arity>` or
  `$shell.capture#<arity>`. The adapter pops exactly that many store text
  values; the first is the program, the rest are the argument list.
- `Run` forwards standard output and standard error and returns the exit status.
  `Capture` forwards standard error and returns `(status, standard output)`.
- The exit status is pushed as a scalar; captured output is pushed as text. Both
  record the last command status in the execution state.
- `ProcessStartInfo.UseShellExecute` is `false`, and every argument is added to
  `ArgumentList`. There is no interpolation, globbing, or metacharacter
  handling. The child inherits the environment, working directory, and standard
  input.
- A missing program, a process that cannot start, a non-text argument, or an
  unattached host is a named `HostFailure`, never an exception and never a
  silent success.

## Scope

Stage 1a has one command per sentence. Pipes, file redirection, environment
changes, signals, and the capability sandbox are later stages; see
`docs/language/action/prose-as-shell.typ`.
