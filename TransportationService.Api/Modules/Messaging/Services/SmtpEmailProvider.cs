using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TransportationService.Api.Modules.Messaging.Configurations;
using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Messaging.Services;

public sealed class SmtpEmailProvider : IEmailProvider
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public SmtpEmailProvider(
        IOptions<SmtpOptions> options,
        ILogger<SmtpEmailProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.RecipientAddress))
        {
            throw new InvalidOperationException(
                "E-mailbericht heeft geen geldig ontvangeradres.");
        }

        var email = new MimeMessage();

        email.From.Add(new MailboxAddress(
            _options.FromName,
            _options.FromAddress));

        email.To.Add(new MailboxAddress(
            message.RecipientName ?? message.RecipientAddress,
            message.RecipientAddress));

        email.Subject = message.Subject ?? string.Empty;

        email.Body = new BodyBuilder
        {
            HtmlBody = message.Body,
        }.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = 30_000,
        };

        try
        {
            var socketOptions = _options.UseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                socketOptions,
                cancellationToken);

            await client.AuthenticateAsync(
                _options.Username,
                _options.Password,
                cancellationToken);

            await client.SendAsync(email, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "E-mailbericht {MessageId} succesvol verzonden.",
                message.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "E-mailbericht {MessageId} kon niet worden verzonden.",
                message.Id);

            throw new InvalidOperationException(
                "SMTP-verzending is mislukt.",
                exception);
        }
    }
}