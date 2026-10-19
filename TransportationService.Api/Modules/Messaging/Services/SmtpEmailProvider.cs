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

        var email = BuildMimeMessage(message, _options);

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

    /// <summary>
    /// H12 (EM-03): the rendered outbox body is PLAIN TEXT (templates use "\n", token values are
    /// raw user input). It goes out as a multipart/alternative: the text/plain part verbatim, and an
    /// HTML part in which every character is HTML-encoded and line breaks become &lt;br&gt;, so a
    /// customer name or portal message can never inject markup into mail sent from the tenant's
    /// domain. The Message-ID is stable per outbox row, so a reviewed re-send of an interrupted
    /// message (see <see cref="MessageDispatcher"/>) can be recognised downstream.
    /// </summary>
    public static MimeMessage BuildMimeMessage(OutboxMessage message, SmtpOptions options)
    {
        var email = new MimeMessage();

        email.From.Add(new MailboxAddress(
            options.FromName,
            options.FromAddress));

        email.To.Add(new MailboxAddress(
            message.RecipientName ?? message.RecipientAddress,
            message.RecipientAddress));

        email.Subject = message.Subject ?? string.Empty;

        var at = options.FromAddress.LastIndexOf('@');
        var domain = at >= 0 && at < options.FromAddress.Length - 1 ? options.FromAddress[(at + 1)..] : "tms.local";
        email.MessageId = $"outbox-{message.Id:N}@{domain}";

        email.Body = new BodyBuilder
        {
            TextBody = message.Body,
            HtmlBody = PlainTextToHtml(message.Body),
        }.ToMessageBody();

        return email;
    }

    /// <summary>Encodes a plain-text body for the HTML alternative: blank lines separate
    /// paragraphs, single line breaks become &lt;br&gt;. Nothing from the input survives as markup.</summary>
    public static string PlainTextToHtml(string? plainText)
    {
        var normalized = (plainText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized
            .Split("\n\n", StringSplitOptions.None)
            .Where(paragraph => paragraph.Trim().Length > 0)
            .Select(paragraph => "<p>" + string.Join("<br>\n",
                paragraph.Trim('\n').Split('\n').Select(System.Net.WebUtility.HtmlEncode)) + "</p>");

        return "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"></head>"
               + "<body style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5;color:#1f2937\">\n"
               + string.Join("\n", paragraphs)
               + "\n</body></html>";
    }
}