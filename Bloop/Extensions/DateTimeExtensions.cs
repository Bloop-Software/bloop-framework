namespace Bloop.Extensions
{
    public static class DateTimeExtensions
    {
        static DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        extension(DateTime dateTime)
        {
            public long ToUnixTimestampWithMilliseconds() => Convert.ToInt64((dateTime - epoch).TotalMilliseconds);
        }
    }
}
