@0x9f3c2a7b5d1e4c60;

struct ActorEnvelope {
  messageId @0 :UInt64;
  target @1 :UInt64;
  sender @2 :UInt64;
  reply @3 :UInt64;
  operation @4 :UInt64;
  bodyKind @5 :UInt64;
  bodyNumber @6 :Float64;
  bodyText @7 :Text;
}
