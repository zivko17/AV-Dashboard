namespace AvDashboard.Core.Settings;

/// <summary>
/// Ajustes donde una directiva de máquina puede pisar la preferencia del usuario.
///
/// Existe porque un actuador mintió. Game DVR se lee de dos sitios —la preferencia en
/// <c>HKCU\System\GameConfigStore\GameDVR_Enabled</c> y la directiva en
/// <c>HKLM\...\Policies\...\GameDVR\AllowGameDVR</c>— y la sonda ya sabía que la directiva
/// manda. El actuador no: escribía la preferencia, leía la preferencia, y anunciaba
/// «off → on» en el Timeline mientras el comportamiento real de la máquina no cambiaba,
/// porque la directiva seguía diciendo que no.
///
/// Es el mismo fallo que dejó siete esquemas de energía duplicados: dar por bueno lo que
/// devuelve una escritura sin comprobar el efecto. La diferencia es que aquí la escritura
/// sí ocurre —la clave cambia— y aun así no sirve de nada.
/// </summary>
public static class PolicyOverride
{
    /// <summary>
    /// El valor efectivo de un ajuste con directiva.
    ///
    /// Una directiva que dice «no» gana siempre: es lo que hace Windows y es lo que el
    /// usuario va a observar. Una directiva ausente deja mandar a la preferencia.
    ///
    /// El «no» del usuario también gana sobre una directiva permisiva: una directiva que
    /// permite algo no lo enciende, solo deja encenderlo.
    /// </summary>
    public static bool? Effective(bool? userPreference, bool? policy) => (userPreference, policy) switch
    {
        (_, false) => false,
        (false, _) => false,
        (null, null) => null,
        _ => true,
    };

    /// <summary>
    /// Si escribir la preferencia del usuario no va a servir de nada.
    ///
    /// Es lo que el actuador tiene que consultar antes de decir que ha aplicado algo. Sin
    /// esto, la fila del Timeline afirma un cambio que la máquina no ha hecho, y el
    /// Timeline existe justamente para poder reconstruir qué pasó de verdad.
    /// </summary>
    public static bool IsBlockedByPolicy(bool? policy, bool desiredValue) =>
        policy == false && desiredValue;

    /// <summary>Lo que se le dice a quien intenta encender algo que una directiva prohíbe.</summary>
    public static string BlockedMessage(string settingName) =>
        $"Una directiva de esta máquina mantiene «{settingName}» desactivado. " +
        "Cambiar la preferencia de usuario no surtiría efecto: hay que quitar la directiva.";
}
