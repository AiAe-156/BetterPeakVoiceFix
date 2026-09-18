using System;
using System.Globalization;
using System.Text;

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

        internal static float PanelWidth(float canvasWidth, float margin) =>
            Math.Max(1f, Math.Min(560f, canvasWidth - margin * 2f));

        // Measure using the actual TMP font and current size; never split a surrogate pair/text element.
        internal static string FitName(string value, float width, int maxWeight, Func<string, float> measure)
        {
            if (string.IsNullOrEmpty(value) || width <= 0) return "";
            var starts = StringInfo.ParseCombiningCharacters(value);
            int weight = 0, count = 0;
            foreach (int start in starts)
            {
                int w = value[start] > 255 ? 2 : 1;
                if (weight + w > maxWeight) break;
                weight += w;
                count++;
            }
            if (count == starts.Length && measure(Literal(value)) <= width) return Literal(value);
            const string dots = "…";
            if (measure(dots) > width) return "";
            // Binary search avoids repeatedly rebuilding the whole panel for long names.
            int low = 0, high = count;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                int end = mid == starts.Length ? value.Length : starts[mid];
                if (measure(Literal(value.Substring(0, end)) + dots) <= width) low = mid;
                else high = mid - 1;
            }
            int length = low == starts.Length ? value.Length : starts[low];
            return Literal(value.Substring(0, length)) + dots;
        }
    }
}
