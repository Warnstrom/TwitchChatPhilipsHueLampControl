using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace TwitchChatHueControls;
public record AuthorizationResult
{
    public required string Code { get; init; }
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthorizationResult Success(string code) =>
        new() { IsSuccess = true, Code = code };
    public static AuthorizationResult Failure(string errorMessage) =>
        new() { IsSuccess = false, Code = string.Empty, ErrorMessage = errorMessage };
}

public interface IWebServer : IAsyncDisposable
{
    Task<AuthorizationResult> WaitForAuthorizationAsync(string state, CancellationToken cancellationToken = default);
}

public sealed class WebServer(IConfiguration configuration) : IWebServer
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly HttpListener _listener = new HttpListener();
    private bool _isDisposed;

    public async Task<AuthorizationResult> WaitForAuthorizationAsync(string state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        var redirectUri = _configuration["RedirectUri"]
            ?? throw new InvalidOperationException("RedirectUri configuration is missing");
        try
        {
            await StartListenerAsync(redirectUri);
            return await ProcessRequestsAsync(state, cancellationToken);
        }
        finally
        {
            StopListener();
        }
    }

    private async Task StartListenerAsync(string redirectUri)
    {
        _listener.Prefixes.Add(redirectUri);
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException($"Failed to start HTTP listener: {ex.Message}", ex);
        }
    }
    private async Task<AuthorizationResult> ProcessRequestsAsync(
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            var result = await HandleRequestAsync(context, expectedState);

            if (result is not null)
            {
                return result;
            }
        }

        return AuthorizationResult.Failure("Listener stopped without receiving valid authorization");
    }
    private static async Task<AuthorizationResult?> HandleRequestAsync(
        HttpListenerContext context,
        string expectedState)
    {
        var query = context.Request.QueryString;
        AuthorizationResult? result = null;
        await using var writer = new StreamWriter(context.Response.OutputStream);
        try
        {
            if (!query.AllKeys.Contains("state"))
            {
                await writer.WriteLineAsync("Missing state parameter");
                return null;
            }

            if (!query["state"].Equals(expectedState))
            {
                AnsiConsole.MarkupLine("[bold red]Request state did not match.[/]");
                await writer.WriteLineAsync("Invalid state parameter");
                return AuthorizationResult.Failure("State mismatch");
            }

            if (!query.AllKeys.Contains("code"))
            {
                await writer.WriteLineAsync("Missing authorization code");
                return AuthorizationResult.Failure("No authorization code provided");
            }

            await writer.WriteLineAsync("Authorization successful! Check your application!");
            result = AuthorizationResult.Success(query["code"]);
        }
        finally
        {
            context.Response.Close();
        }
        return result;
    }

    private void StopListener()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        StopListener();
        _listener.Close();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}