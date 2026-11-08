module Arena

open Types

/// A stable, storable reference into a managed arena. Struct annotations are
/// host performance choices, not semantic guarantees (D80).
[<Struct>]
type TensorHandle =
    {
        Arena: int
        Offset: int
        Length: int
        Rank: int
        Generation: int
    }

/// A transient, stack-only projection over a tensor's storage. It cannot be
/// stored in a record, option, class, closure, or persistence.
[<Struct; System.Runtime.CompilerServices.IsByRefLike>]
type TensorView =
    val Data: System.ReadOnlySpan<double>
    val Shape: System.ReadOnlySpan<int>
    new(data, shape) = { Data = data; Shape = shape }

/// A managed backing arena. Execution state: never serialized.
type ManagedArena =
    {
        Generation: int
        Tensors: Map<int, Tensor>
        NextSlot: int
    }

let empty: ManagedArena =
    {
        Generation = 0
        Tensors = Map.empty
        NextSlot = 0
    }

/// Allocates a tensor and returns a stable handle. The handle names the
/// generation it was allocated in, so closing the arena invalidates it.
let allocate (tensor: Tensor) (arena: ManagedArena) : TensorHandle * ManagedArena =
    let slot = arena.NextSlot

    let handle =
        {
            Arena = 0
            Offset = slot
            Length = Tensor.size tensor
            Rank = Tensor.rank tensor
            Generation = arena.Generation
        }

    (handle,
     { arena with
         Tensors = Map.add slot tensor arena.Tensors
         NextSlot = slot + 1
     })

let resolve (handle: TensorHandle) (arena: ManagedArena) : Tensor option =
    if handle.Arena <> 0 || handle.Generation <> arena.Generation then
        None
    else
        Map.tryFind handle.Offset arena.Tensors

let isLive (handle: TensorHandle) (arena: ManagedArena) : bool =
    resolve handle arena |> Option.isSome

let viewOf (tensor: Tensor) : TensorView =
    TensorView(System.ReadOnlySpan<double>(tensor.Data), System.ReadOnlySpan<int>(tensor.Shape))

/// Closing an arena invalidates every handle from its previous generation.
let close (arena: ManagedArena) : ManagedArena =
    {
        Generation = arena.Generation + 1
        Tensors = Map.empty
        NextSlot = 0
    }
