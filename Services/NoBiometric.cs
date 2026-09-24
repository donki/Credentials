namespace Credentials.Services;

/// <summary>Sin verificacion biometrica: en Windows la boveda se abre solo con la contraseña maestra.</summary>
public sealed class NoBiometric : IBiometric
{
    public Task<bool> IsAvailableAsync() => Task.FromResult(false);

    public Task<bool> AuthenticateAsync(string title, string reason) => Task.FromResult(false);
}
