namespace Bloop.WebApi
{
    // Validates that an authenticated token's session is still active (exists, belongs
    // to the user, not expired/revoked). Applications register an implementation so
    // request handling enforces login session management, not just token integrity.
    public interface ISessionValidator
    {
        Task ValidateSessionAsync(Guid userId, Guid sessionId);
    }
}
