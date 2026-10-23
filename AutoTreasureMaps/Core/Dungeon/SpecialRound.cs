namespace AutoTreasureMaps.Core.Dungeon;

// The special rounds a treasure dungeon can turn up between its draws. Vault Oneiron has one only, its sack round
// (奇梦抚心): a hundred glowing sacks asked for in ninety seconds.
internal enum SpecialRoundKind : byte
{
    SackRound,
}

// A special round met in a dungeon and how it went: for the sack round, the party's sack count at its end.
internal readonly record struct SpecialRound(SpecialRoundKind Kind, int Score);
