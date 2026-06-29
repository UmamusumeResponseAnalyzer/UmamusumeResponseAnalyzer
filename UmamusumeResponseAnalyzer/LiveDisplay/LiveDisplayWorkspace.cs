namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    public sealed class LiveDisplayWorkspace : IEquatable<LiveDisplayWorkspace>
    {
        static readonly StringComparer IdComparer = StringComparer.Ordinal;

        LiveDisplayWorkspace(string id, string title)
        {
            Id = Normalize(id, nameof(id));
            Title = Normalize(title, nameof(title));
        }

        public string Id { get; }
        public string Title { get; }

        public static LiveDisplayWorkspace Create(string id, string title)
        {
            return new LiveDisplayWorkspace(id, title);
        }

        public bool Equals(LiveDisplayWorkspace? other)
        {
            return other is not null && IdComparer.Equals(Id, other.Id);
        }

        public override bool Equals(object? obj)
        {
            return obj is LiveDisplayWorkspace other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IdComparer.GetHashCode(Id);
        }

        public override string ToString()
        {
            return Title;
        }

        public static bool operator ==(LiveDisplayWorkspace? left, LiveDisplayWorkspace? right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        public static bool operator !=(LiveDisplayWorkspace? left, LiveDisplayWorkspace? right)
        {
            return !(left == right);
        }

        static string Normalize(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Workspace id/title 不能为空。", parameterName);

            return value.Trim();
        }
    }

    public enum LiveDisplaySeverity
    {
        Trace,
        Info,
        Success,
        Warning,
        Error
    }
}
