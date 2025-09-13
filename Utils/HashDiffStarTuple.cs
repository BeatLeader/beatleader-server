using ProtoBuf;

namespace BeatLeader_Server.Utils;

[ProtoContract(SkipConstructor = true)]
public record struct HashDiffStarTuple([property: ProtoMember(1)] string Hash, [property: ProtoMember(2)] string Diff, [property: ProtoMember(3)] float Stars);