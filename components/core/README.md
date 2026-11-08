# Scale core

The shared foundation: runtime and image types, pattern/expression/package
ASTs and portable documents, the vocabulary, the parser and tokenizer, the
arena/reclaim types, and persistence. It has no evaluator and no host
knowledge.

`Scale.Core.fsproj`; the public interface is its modules. The kernel is a
separate component above the ABI.
