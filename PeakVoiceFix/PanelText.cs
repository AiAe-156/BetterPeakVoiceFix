using System;

namespace PeakVoiceFix
{
    // Display-only helpers. Never use the returned strings as network room/player names.
    internal static class PanelText
    {
        // Escape each opening bracket separately, including a user-supplied </noparse>.
        internal static string Literal(string value) => (value ?? "").Replace("<", "<noparse><</noparse>");

        internal static bool? RoomNamesMatch(string gameRoom, string voiceRoom) =>
            string.IsNullOrEmpty(gameRoom) || string.IsNullOrEmpty(voiceRoom) ? (bool?)null
            : string.Equals(voiceRoom, gameRoom + "_voice_", StringComparison.Ordinal);
    }
}
