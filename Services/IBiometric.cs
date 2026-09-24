namespace Credentials.Services;

/// <summary>
/// Huella o cara en Android (en Windows no hay: <see cref="NoBiometric"/>): solo dice «es el usuario». La clave de la
/// boveda la guarda SecureStorage; esto es la puerta para leerla sin escribir la contraseña.
/// </summary>
public interface IBiometric
{
    Task<bool> IsAvailableAsync();

    /// <summary>Pide la verificacion al sistema. True si el usuario paso.</summary>
    Task<bool> AuthenticateAsync(string title, string reason);
}
