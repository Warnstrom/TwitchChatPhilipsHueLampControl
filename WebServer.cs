using System.Net;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

public class Authorization
{
    public string Code { get; }

    public Authorization(string code)
    {
        Code = code;
    }
}

public interface IWebServer : IDisposable
{
    public Task<Authorization?> ListenAsync(string state);
    public void Dispose();

}

public class WebServer : IWebServer, IDisposable
{
    private HttpListener _listener;
    private readonly IConfiguration _configuration;
    private bool _disposed = false;
    private string _state;

    public WebServer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<Authorization?> ListenAsync(string state)
    {
        _state = state;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_configuration["RedirectUri"]);
        _listener.Start();
        try
        {
            return await OnRequestAsync();
        }
        finally
        {
            Stop();
        }
    }

    private async Task<Authorization?> OnRequestAsync()
    {
        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            using (var writer = new StreamWriter(response.OutputStream))
            {
                if (request.QueryString.AllKeys.Contains("state"))
                {
                    if (request.QueryString["state"].Equals(_state))
                    {
                        if (request.QueryString.AllKeys.Contains("code"))
                        {
                            writer.WriteLine("Authorization successful! Check your application!");
                            writer.Flush();
                            return new Authorization(request.QueryString["code"]);
                        }
                        else
                        {
                            writer.WriteLine("No code found in query string!");
                            writer.Flush();
                        }
                    }
                    else
                    {   
                        AnsiConsole.MarkupLine($"[bold red]Request state did not match.[/]");
                        return null;
                    }
                }



            }

            // Close the response to prevent further output to the stream
            response.Close();
        }
        return null;
    }

    private void Stop()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
        _listener.Close();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
                _listener?.Close();
            }
            _disposed = true;
        }
    }
    ~WebServer()
    {
        Dispose(false);
    }
}
