module KernelState

open Types
open LoweredInstructions

/// The kernel's execution state: the store, the operand stack, the pending
/// program, and one opaque host value. The host value is how the Prelude
/// adapter threads its own execution state without the kernel knowing any
/// Prelude type. Store, Stack, and Program are authoritative; the host value
/// is only carried between primitive calls.
type KernelState<'Host> =
    {
        Host: 'Host
        Store: Store
        Stack: EntryIdentity list
        Program: string list
        /// Named association-vector roots. The root value is an entry identity;
        /// `None` means the named root is not set.
        Roots: Map<string, EntryIdentity option>
        /// Words defined from quotation text at run time. A dynamic word shadows
        /// an image unit of the same name.
        Words: Map<string, Instruction list>
        /// Captured values for the active closure call, innermost first.
        CaptureFrames: EntryIdentity list list
        /// Positions recorded by `RecordStep`, newest first (D201, superseding D120). Execution
        /// state, never serialized.
        History: int list
    }

let create (host: 'Host) (store: Store) (stack: EntryIdentity list) (program: string list) : KernelState<'Host> =
    {
        Host = host
        Store = store
        Stack = stack
        Program = program
        Roots = Map.empty
        Words = Map.empty
        CaptureFrames = []
        History = []
    }
