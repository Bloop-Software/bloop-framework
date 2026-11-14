using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Bloop.Client.Desktop
{
    public static class DesktopSecureStore
    {
        // Picks the operating system's credential store. Linux, and macOS builds that are
        // not code-signed (where the Keychain prompts on every launch), fall back to a
        // file only the current user can read.
        public static ISecureStore Create(string applicationName, bool preferOperatingSystemStore = true)
        {
            if (preferOperatingSystemStore && OperatingSystem.IsMacOS())
                return new MacKeychainSecureStore(applicationName);

            if (OperatingSystem.IsWindows())
                return new WindowsProtectedSecureStore(applicationName);

            return new FileSecureStore(applicationName);
        }

        internal static string DataDirectory(string applicationName)
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), applicationName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static string FileNameFor(string key) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
    }

    // Values are stored unencrypted, protected only by file permissions. A development
    // fallback, not a credential vault.
    public sealed class FileSecureStore(string applicationName) : ISecureStore
    {
        private readonly string _directory = Path.Combine(DesktopSecureStore.DataDirectory(applicationName), "store");

        public async Task<string?> GetAsync(string key)
        {
            var path = PathFor(key);
            return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        }

        public async Task SetAsync(string key, string value)
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(key);

            await File.WriteAllTextAsync(path, value);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        public Task RemoveAsync(string key)
        {
            File.Delete(PathFor(key));
            return Task.CompletedTask;
        }

        private string PathFor(string key) => Path.Combine(_directory, DesktopSecureStore.FileNameFor(key));
    }

    [SupportedOSPlatform("windows")]
    public sealed class WindowsProtectedSecureStore(string applicationName) : ISecureStore
    {
        private readonly string _directory = Path.Combine(DesktopSecureStore.DataDirectory(applicationName), "store");

        public async Task<string?> GetAsync(string key)
        {
            var path = PathFor(key);
            if (!File.Exists(path))
                return null;

            try
            {
                var plaintext = ProtectedData.Unprotect(
                    await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        public async Task SetAsync(string key, string value)
        {
            Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(
                PathFor(key),
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }

        public Task RemoveAsync(string key)
        {
            File.Delete(PathFor(key));
            return Task.CompletedTask;
        }

        private string PathFor(string key) => Path.Combine(_directory, DesktopSecureStore.FileNameFor(key));
    }

    [SupportedOSPlatform("macos")]
    public sealed partial class MacKeychainSecureStore(string serviceName) : ISecureStore
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const int ErrSecSuccess = 0;
        private const int ErrSecItemNotFound = -25300;

        [LibraryImport(Security)]
        private static partial int SecKeychainFindGenericPassword(
            IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account,
            out uint passwordLength, out IntPtr passwordData, out IntPtr item);

        [LibraryImport(Security)]
        private static partial int SecKeychainAddGenericPassword(
            IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account,
            uint passwordLength, byte[] password, IntPtr item);

        [LibraryImport(Security)]
        private static partial int SecKeychainItemModifyAttributesAndData(
            IntPtr item, IntPtr attributes, uint length, byte[] data);

        [LibraryImport(Security)]
        private static partial int SecKeychainItemDelete(IntPtr item);

        [LibraryImport(Security)]
        private static partial int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

        [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static partial void CFRelease(IntPtr reference);

        private readonly byte[] _service = Encoding.UTF8.GetBytes(serviceName);

        public Task<string?> GetAsync(string key)
        {
            var account = Encoding.UTF8.GetBytes(key);
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out var length, out var data, out var item);

            if (status == ErrSecItemNotFound)
                return Task.FromResult<string?>(null);

            ThrowIfFailed(status);

            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }
        }

        public Task SetAsync(string key, string value)
        {
            var account = Encoding.UTF8.GetBytes(key);
            var password = Encoding.UTF8.GetBytes(value);

            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out _, out var data, out var item);

            if (status == ErrSecItemNotFound)
            {
                ThrowIfFailed(SecKeychainAddGenericPassword(
                    IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                    (uint)password.Length, password, IntPtr.Zero));
                return Task.CompletedTask;
            }

            ThrowIfFailed(status);

            try
            {
                ThrowIfFailed(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password));
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            var account = Encoding.UTF8.GetBytes(key);
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)_service.Length, _service, (uint)account.Length, account,
                out _, out var data, out var item);

            if (status == ErrSecItemNotFound)
                return Task.CompletedTask;

            ThrowIfFailed(status);

            try
            {
                ThrowIfFailed(SecKeychainItemDelete(item));
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }

            return Task.CompletedTask;
        }

        private static void ThrowIfFailed(int status)
        {
            if (status != ErrSecSuccess)
                throw new InvalidOperationException($"Keychain operation failed with status {status}");
        }
    }
}
