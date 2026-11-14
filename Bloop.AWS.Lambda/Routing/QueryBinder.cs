using Bloop.WebApi;
using System.Globalization;
using System.Reflection;

namespace Bloop.AWS.Lambda.Routing
{
    // Binds query-string values onto a record's primary constructor. Parameters that are
    // nullable or have a default are optional; everything else is required.
    internal static class QueryBinder
    {
        private static readonly MethodInfo ParseMethod =
            typeof(QueryBinder).GetMethod(nameof(TryParse), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly NullabilityInfoContext Nullability = new();

        public static T Bind<T>(IReadOnlyDictionary<string, string> query)
        {
            var constructor = typeof(T).GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"{typeof(T).Name} has no public constructor");

            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var name = parameter.Name!;

                if (query.TryGetValue(name, out var raw) && !string.IsNullOrEmpty(raw))
                    arguments[i] = Convert(raw, parameter.ParameterType, name);
                else if (parameter.HasDefaultValue)
                    arguments[i] = parameter.DefaultValue;
                else if (IsNullable(parameter))
                    arguments[i] = null;
                else
                    throw new WebApiException($"Query parameter '{name}' is required");
            }

            return (T)constructor.Invoke(arguments);
        }

        private static bool IsNullable(ParameterInfo parameter)
        {
            if (Nullable.GetUnderlyingType(parameter.ParameterType) != null)
                return true;

            if (parameter.ParameterType.IsValueType)
                return false;

            lock (Nullability)
                return Nullability.Create(parameter).WriteState == NullabilityState.Nullable;
        }

        private static object Convert(string raw, Type type, string name)
        {
            var elementType = GetElementType(type);
            if (elementType != null)
            {
                var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var array = Array.CreateInstance(elementType, parts.Length);
                for (var i = 0; i < parts.Length; i++)
                    array.SetValue(ConvertScalar(parts[i], elementType, name), i);
                return array;
            }

            return ConvertScalar(raw, type, name);
        }

        private static Type? GetElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType && type != typeof(string))
            {
                var element = type.GetGenericArguments()[0];
                if (type.IsAssignableFrom(element.MakeArrayType()))
                    return element;
            }

            return null;
        }

        private static object ConvertScalar(string raw, Type type, string name)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;

            if (target == typeof(string))
                return raw;

            if (target.IsEnum)
            {
                if (Enum.TryParse(target, raw, ignoreCase: true, out var enumValue) && Enum.IsDefined(target, enumValue!))
                    return enumValue!;
                throw Invalid(name);
            }

            // DateTime.Parse would convert a UTC instant to local time; keep it in UTC.
            if (target == typeof(DateTime))
            {
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dateTime))
                    return dateTime;
                throw Invalid(name);
            }

            var parsable = typeof(IParsable<>).MakeGenericType(target);
            if (!parsable.IsAssignableFrom(target))
                throw new InvalidOperationException(
                    $"Query parameter '{name}' has unsupported type {target.Name}");

            return ParseMethod.MakeGenericMethod(target).Invoke(null, [raw]) ?? throw Invalid(name);
        }

        private static object? TryParse<T>(string raw) where T : IParsable<T> =>
            T.TryParse(raw, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static WebApiException Invalid(string name) =>
            new($"Query parameter '{name}' is invalid");
    }
}
