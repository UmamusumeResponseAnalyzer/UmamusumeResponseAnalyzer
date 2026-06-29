namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    static class PopupCountdown
    {
        public static string Format(DateTimeOffset? expiresAt, DateTimeOffset now)
        {
            if (expiresAt is null)
                return string.Empty;

            var remaining = expiresAt.Value - now;
            if (remaining <= TimeSpan.Zero)
                return "0s";

            return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s";
        }
    }
}
