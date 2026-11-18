namespace Bloop.AWS.Lambda.Routing
{
    internal sealed class RouteTemplate
    {
        private readonly (string Value, bool IsParameter)[] _segments;

        private RouteTemplate(string text, (string Value, bool IsParameter)[] segments)
        {
            Text = text;
            _segments = segments;
            LiteralCount = segments.Count(s => !s.IsParameter);
        }

        public string Text { get; }

        public int Length => _segments.Length;

        public int LiteralCount { get; }

        public static RouteTemplate Parse(string template)
        {
            var parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var segments = new (string, bool)[parts.Length];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isParameter = part.Length > 2 && part[0] == '{' && part[^1] == '}';

                if (!isParameter && (part.Contains('{') || part.Contains('}')))
                    throw new ArgumentException(
                        $"Route '{template}': a parameter must span a whole segment", nameof(template));

                var value = isParameter ? part[1..^1] : part;
                if (isParameter && !names.Add(value))
                    throw new ArgumentException(
                        $"Route '{template}': duplicate parameter '{value}'", nameof(template));

                segments[i] = (value, isParameter);
            }

            // A leading literal anchors the template when an emulator prefixes the path
            // with its own stage segments.
            if (segments.Length == 0 || segments[0].Item2)
                throw new ArgumentException(
                    $"Route '{template}' must start with a literal segment", nameof(template));

            return new RouteTemplate(template, segments);
        }

        public bool TryMatch(string[] pathSegments, int offset, out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (pathSegments.Length - offset != _segments.Length)
                return false;

            for (var i = 0; i < _segments.Length; i++)
            {
                var (value, isParameter) = _segments[i];
                var actual = pathSegments[offset + i];

                if (isParameter)
                    values[value] = Uri.UnescapeDataString(actual);
                else if (!value.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
