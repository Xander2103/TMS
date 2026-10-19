using MimeKit;
using TransportationService.Api.Modules.Messaging.Configurations;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// H12 (EM-03/EM-04): plain-text outbox bodies leave as multipart/alternative with a verbatim
/// text/plain part and an HTML part in which user-entered values are encoded (no markup
/// injection) and line breaks survive; the invoice mail no longer promises an attachment.
/// </summary>
public class SmtpEmailBodyTests
{
    private static readonly SmtpOptions Options = new()
    {
        Host = "smtp.example.be", Username = "u", Password = "p",
        FromAddress = "noreply@transport.example.be", FromName = "Transport BV",
    };

    private static OutboxMessage Message(string body) => new()
    {
        Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
        Channel = MessageChannel.Email,
        Kind = MessageKinds.CustomerMessageReply,
        RecipientAddress = "klant@haven.be",
        RecipientName = "Haven BV",
        Subject = "Nieuw bericht",
        Body = body,
    };

    [Fact]
    public void UserEnteredMarkup_IsEncodedInTheHtmlPart_AndVerbatimInTheTextPart()
    {
        const string body =
            "Beste <script>alert('x')</script> & Co,\n\n\"Levering\" om 10u <b>vet</b>\nTweede regel\n\nGroeten,\nTransport & Zonen";

        var mime = SmtpEmailProvider.BuildMimeMessage(Message(body), Options);

        Assert.Equal(body, mime.TextBody);
        var html = mime.HtmlBody;
        Assert.NotNull(html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; Co", html);
        Assert.Contains("&quot;Levering&quot; om 10u &lt;b&gt;vet&lt;/b&gt;<br>", html);
        Assert.Contains("Tweede regel", html);
        Assert.Contains("Transport &amp; Zonen", html);
        // Blank lines separate paragraphs; single line breaks become <br>.
        Assert.Equal(3, html.Split("<p>").Length - 1);
    }

    [Fact]
    public void Message_IsMultipartAlternative_WithAStablePerRowMessageId()
    {
        var mime = SmtpEmailProvider.BuildMimeMessage(Message("Hallo\nwereld"), Options);

        var alternative = Assert.IsType<MultipartAlternative>(mime.Body);
        Assert.Contains(alternative, part => part.ContentType.IsMimeType("text", "plain"));
        Assert.Contains(alternative, part => part.ContentType.IsMimeType("text", "html"));
        Assert.Equal("outbox-6f9619ff8b86d011b42d00cf4fc964ff@transport.example.be", mime.MessageId);
        Assert.Equal("Haven BV", mime.To.Mailboxes.Single().Name);
    }

    [Fact]
    public void PlainTextToHtml_HandlesEmptyAndCarriageReturns()
    {
        Assert.DoesNotContain("<p>", SmtpEmailProvider.PlainTextToHtml(null));
        var html = SmtpEmailProvider.PlainTextToHtml("a\r\nb\r\n\r\nc");
        Assert.Contains("<p>a<br>\nb</p>", html);
        Assert.Contains("<p>c</p>", html);
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("en")]
    public void InvoiceMails_NoLongerPromiseAnAttachment(string language)
    {
        foreach (var kind in new[] { MessageKinds.InvoiceSent, MessageKinds.InvoiceCreditNote })
        {
            var template = BuiltInMessageTemplates.Resolve(kind, MessageChannel.Email, language);
            Assert.NotNull(template);
            var body = template.Body.ToLowerInvariant();
            Assert.DoesNotContain("bijlage", body);
            Assert.DoesNotContain("ci-joint", body);
            Assert.DoesNotContain("attached", body);
        }

        var invoice = BuiltInMessageTemplates.Resolve(MessageKinds.InvoiceSent, MessageChannel.Email, language).Body;
        Assert.Contains(language switch { "fr" => "portail client", "en" => "customer portal", _ => "klantportaal" }, invoice);
    }
}
