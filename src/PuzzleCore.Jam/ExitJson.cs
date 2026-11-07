using System.Collections.Generic;
using PuzzleCore.Json;
using PuzzleCore.Records;

namespace PuzzleCore.Jam
{
    /// <summary>The Jam Move payload in pc-record: { "t": "Move", "piece": &lt;PieceId 0-23&gt;, ... }.</summary>
    public sealed class ExitJson : IMoveJson<Exit>
    {
        public static ExitJson Instance { get; } = new ExitJson();

        private ExitJson()
        {
        }

        public GameKind Game => GameKind.Jam;

        public IReadOnlyList<JsonMember> Write(Exit move) => new[] { new JsonMember("piece", new JsonInteger(move.Piece.Value)) };

        public Option<Exit> Read(JsonFields fields)
        {
            long? piece = fields.RequiredInteger("piece", 0, PieceId.MaxValue);
            return piece is null ? Option.None<Exit>() : Option.Some(new Exit(new PieceId((byte)piece.Value)));
        }
    }
}
