using System.Security.Cryptography;
using System.Text;
using AvDashboard.Core.Contracts;

namespace AvDashboard.Agent.Auth;

/// <summary>
/// Autenticacion por token bearer para toda la API. Sin token valido no se responde
/// nada, ni siquiera la lista de modulos: es el agente el que corre como LocalSystem.
/// </summary>
public sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _expected;

    public BearerTokenMiddleware(RequestDelegate next, TokenStore tokens)
    {
        _next = next;
        _expected = Encoding.UTF8.GetBytes(tokens.Token);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // El endpoint de salud no lleva token: la consola lo usa para saber si el agente
        // esta vivo antes de pedirle credenciales al usuario. No revela nada.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!TryGetToken(context.Request, out string? presented) || !IsValid(presented))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";

            await context.Response.WriteAsJsonAsync(new ApiError
            {
                Code = "unauthorized",
                Message = "Token de agente ausente o incorrecto.",
            }).ConfigureAwait(false);

            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool TryGetToken(HttpRequest request, out string? token)
    {
        token = null;

        if (request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues header))
        {
            string? value = header.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = value["Bearer ".Length..].Trim();
                return token.Length > 0;
            }
        }

        // SignalR con WebSockets no puede poner cabeceras en el handshake del navegador,
        // asi que acepta el token por query string. Es la excepcion documentada.
        if (request.Query.TryGetValue("access_token", out Microsoft.Extensions.Primitives.StringValues queryToken))
        {
            token = queryToken.ToString();
            return token.Length > 0;
        }

        return false;
    }

    /// <summary>Comparacion en tiempo constante: un token no se compara con string.Equals.</summary>
    private bool IsValid(string? presented)
    {
        if (string.IsNullOrEmpty(presented) || _expected.Length == 0)
        {
            return false;
        }

        byte[] candidate = Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(candidate, _expected);
    }
}
