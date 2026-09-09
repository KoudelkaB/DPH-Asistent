using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dph.Core.Isds;

public sealed record IsdsCredentials(string Login, string Password);

public sealed record IsdsOwner(string DataBoxId, string Name, string BoxType);

public sealed record IsdsAttachment(string FileName, byte[] Content, string MimeType = "application/xml");

public sealed record IsdsSentMessage(string MessageId, string StatusMessage);

/// <summary>Záznam ze seznamu odeslaných zpráv; slouží k dohledání zprávy po nejistém odeslání.</summary>
public sealed record IsdsSentMessageInfo(string MessageId, string SenderReference, string RecipientDataBoxId);

// Chyba vrácená ISDS (dmStatusCode/dbStatusCode != 0000) nebo chyba přenosu. StatusCode je prázdný,
// když se odpověď vůbec nepodařilo přečíst.
public sealed class IsdsException(string message, string statusCode = "", Exception? inner = null)
    : Exception(message, inner)
{
    public string StatusCode { get; } = statusCode;

    // ISDS vrací 401 i při dočasném zablokování účtu; heslo si necháme přepsat vždy, když
    // autentizace selže, ať uživatel neopakuje odesílání se špatnými údaji.
    public bool IsAuthenticationFailure { get; init; }

    /// <summary>
    /// Požadavek se nepodařilo dokončit tak, aby bylo jasné, co s ním server udělal – výpadek
    /// spojení, vypršení časového limitu, nečitelná odpověď nebo chyba 5xx. U odeslání zprávy to
    /// znamená, že podání <b>mohlo</b> vzniknout; opakovat se smí až po ověření v ISDS.
    /// Naopak stavový kód ISDS nebo odmítnutí 401/403 je jednoznačné – zpráva nevznikla.
    /// </summary>
    public bool IsOutcomeUnknown { get; init; }
}

public interface IIsdsClient
{
    Task<IsdsOwner> GetOwnerAsync(IsdsCredentials credentials, CancellationToken cancellationToken = default);

    Task<IsdsSentMessage> CreateMessageAsync(
        IsdsCredentials credentials,
        string recipientDataBoxId,
        string annotation,
        string senderReference,
        IReadOnlyList<IsdsAttachment> attachments,
        CancellationToken cancellationToken = default);

    /// <summary>Zprávy odeslané v zadaném okně – pro dohledání zprávy, u které se ztratila odpověď.</summary>
    Task<IReadOnlyList<IsdsSentMessageInfo>> GetSentMessagesAsync(
        IsdsCredentials credentials,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadSignedSentMessageAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadSignedDeliveryInfoAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Klient webových služeb ISDS (datové schránky) s přihlášením jménem a heslem přes HTTP Basic.
/// Schránky zabezpečené jednorázovým heslem (SMS/TOTP) mají jiné rozhraní a nejsou podporované –
/// projeví se chybou autentizace.
/// </summary>
public sealed class IsdsClient(HttpClient httpClient) : IIsdsClient
{
    // Endpointy dle WSDL v3.11 (od 3/2026 doména datovka.gov.cz, dřív mojedatovaschranka.cz).
    private const string MessageOperationsUrl = "https://ws1.datovka.gov.cz/DS/dz";   // dm_operations
    private const string MessageInfoUrl = "https://ws1.datovka.gov.cz/DS/dx";         // dm_info
    private const string DataBoxAccessUrl = "https://ws1.datovka.gov.cz/DS/DsManage"; // db_access

    private static readonly XNamespace Isds = "http://isds.czechpoint.cz/v20";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private const string SuccessStatus = "0000";

    // Zpráva smí mít nejvýš 50 MB příloh; XML přiznání je o několik řádů menší, kontrolu děláme
    // jen proto, aby se místo obskurní chyby serveru ukázalo srozumitelné hlášení.
    private const int MaxAttachmentBytes = 50 * 1024 * 1024;

    public async Task<IsdsOwner> GetOwnerAsync(IsdsCredentials credentials, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            DataBoxAccessUrl,
            credentials,
            new XElement(Isds + "GetOwnerInfoFromLogin", new XElement(Isds + "dbDummy", "")),
            "dbStatus",
            cancellationToken);

        var owner = response.Element(Isds + "dbOwnerInfo");
        var name = Text(owner?.Element(Isds + "firmName"));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.Join(' ', new[]
            {
                Text(owner?.Element(Isds + "pnFirstName")),
                Text(owner?.Element(Isds + "pnLastName"))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return new IsdsOwner(
            Text(owner?.Element(Isds + "dbID")),
            name,
            Text(owner?.Element(Isds + "dbType")));
    }

    public async Task<IsdsSentMessage> CreateMessageAsync(
        IsdsCredentials credentials,
        string recipientDataBoxId,
        string annotation,
        string senderReference,
        IReadOnlyList<IsdsAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        if (!TaxOfficeDataBoxes.IsValidId(recipientDataBoxId))
        {
            throw new IsdsException($"Neplatné ID datové schránky příjemce „{recipientDataBoxId}“ – musí mít 7 znaků.");
        }

        if (attachments.Count == 0)
        {
            throw new IsdsException("Datová zpráva musí mít alespoň jednu přílohu.");
        }

        if (attachments.Sum(x => (long)x.Content.Length) > MaxAttachmentBytes)
        {
            throw new IsdsException("Přílohy přesahují limit datové zprávy 50 MB.");
        }

        var request = new XElement(Isds + "CreateMessage",
            new XElement(Isds + "dmEnvelope", BuildEnvelope(recipientDataBoxId, annotation, senderReference)),
            BuildFiles(attachments));

        var response = await CallAsync(MessageOperationsUrl, credentials, request, "dmStatus", cancellationToken);

        var messageId = Text(response.Element(Isds + "dmID"));
        if (string.IsNullOrWhiteSpace(messageId))
        {
            // Stav 0000 znamená, že ISDS podání přijalo – zpráva tedy nejspíš vznikla, jen neznáme
            // její ID. Opakovat odeslání se nesmí, dokud se nedohledá podle značky odesílatele.
            throw new IsdsException("ISDS potvrdil odeslání, ale nevrátil ID datové zprávy.", SuccessStatus)
            {
                IsOutcomeUnknown = true
            };
        }

        return new IsdsSentMessage(messageId, StatusMessage(response, "dmStatus"));
    }

    private const int SentMessagePageSize = 1000;

    // Strop proti nekonečné smyčce, kdyby server stránkování ignoroval. Při jeho dosažení raději
    // ohlásíme nejistotu, než abychom vrátili neúplný seznam – z neúplného seznamu by se dalo
    // usoudit „zpráva neexistuje“ a podání by se odeslalo podruhé.
    private const int MaxSentMessagePages = 50;

    public async Task<IReadOnlyList<IsdsSentMessageInfo>> GetSentMessagesAsync(
        IsdsCredentials credentials,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var all = new List<IsdsSentMessageInfo>();
        // dmOffset je 1-based; stránkuje se, dokud server vrací plnou stránku.
        for (var page = 0; page < MaxSentMessagePages; page++)
        {
            var request = new XElement(Isds + "GetListOfSentMessages",
                new XElement(Isds + "dmFromTime", XmlConvert.ToString(from.ToUniversalTime())),
                new XElement(Isds + "dmToTime", XmlConvert.ToString(to.ToUniversalTime())),
                Nil("dmSenderOrgUnitNum"),
                // -1 = bez filtru na stav zprávy; hledáme i zprávy, které mezitím došly a byly doručeny.
                new XElement(Isds + "dmStatusFilter", "-1"),
                new XElement(Isds + "dmOffset", (page * SentMessagePageSize + 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(Isds + "dmLimit", SentMessagePageSize.ToString(CultureInfo.InvariantCulture)));

            var response = await CallAsync(MessageInfoUrl, credentials, request, "dmStatus", cancellationToken);
            var records = response.Element(Isds + "dmRecords")?.Elements(Isds + "dmRecord").ToArray() ?? [];
            all.AddRange(records.Select(record => new IsdsSentMessageInfo(
                Text(record.Element(Isds + "dmID")),
                Text(record.Element(Isds + "dmSenderRefNumber")),
                Text(record.Element(Isds + "dbIDRecipient")))));

            if (records.Length < SentMessagePageSize)
            {
                return all;
            }
        }

        throw new IsdsException(
            $"Seznam odeslaných zpráv je delší než {MaxSentMessagePages * SentMessagePageSize} položek a nejde spolehlivě prohledat.")
        {
            IsOutcomeUnknown = true
        };
    }

    public Task<byte[]?> DownloadSignedSentMessageAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
        => DownloadSignatureAsync(MessageOperationsUrl, "SignedSentMessageDownload", credentials, messageId, cancellationToken);

    public Task<byte[]?> DownloadSignedDeliveryInfoAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
        => DownloadSignatureAsync(MessageInfoUrl, "GetSignedDeliveryInfo", credentials, messageId, cancellationToken);

    private async Task<byte[]?> DownloadSignatureAsync(
        string url,
        string operation,
        IsdsCredentials credentials,
        string messageId,
        CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            url,
            credentials,
            new XElement(Isds + operation, new XElement(Isds + "dmID", messageId)),
            "dmStatus",
            cancellationToken);

        var signature = Text(response.Element(Isds + "dmSignature"));
        return string.IsNullOrWhiteSpace(signature) ? null : Convert.FromBase64String(signature);
    }

    // Obálka odesílané zprávy. Prvky skupiny gMessageEnvelopeSub jsou v XSD povinné (byť nillable),
    // proto se posílají všechny v pořadí ze schématu; nevyplněné jako xsi:nil.
    private static IEnumerable<XElement> BuildEnvelope(string recipientDataBoxId, string annotation, string senderReference)
    {
        yield return Nil("dmSenderOrgUnit");
        yield return Nil("dmSenderOrgUnitNum");
        yield return new XElement(Isds + "dbIDRecipient", recipientDataBoxId);
        yield return Nil("dmRecipientOrgUnit");
        yield return Nil("dmRecipientOrgUnitNum");
        yield return Nil("dmToHands");
        yield return new XElement(Isds + "dmAnnotation", Truncate(annotation, 255));
        yield return Nil("dmRecipientRefNumber");
        // Naše jednoznačná značka podání. Vrací se i v seznamu odeslaných zpráv, takže se podle ní
        // dá zpráva dohledat, když se ztratí odpověď na CreateMessage.
        yield return new XElement(Isds + "dmSenderRefNumber", Truncate(senderReference, 50));
        yield return Nil("dmRecipientIdent");
        yield return Nil("dmSenderIdent");
        yield return Nil("dmLegalTitleLaw");
        yield return Nil("dmLegalTitleYear");
        yield return Nil("dmLegalTitleSect");
        yield return Nil("dmLegalTitlePar");
        yield return Nil("dmLegalTitlePoint");
        yield return new XElement(Isds + "dmPersonalDelivery", "false");
        yield return new XElement(Isds + "dmAllowSubstDelivery", "true");
    }

    private static XElement BuildFiles(IReadOnlyList<IsdsAttachment> attachments)
    {
        var files = new XElement(Isds + "dmFiles");
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            files.Add(new XElement(Isds + "dmFile",
                new XAttribute("dmMimeType", attachment.MimeType),
                // ISDS vyžaduje, aby první písemnost byla „main“; ostatní jsou přílohy.
                new XAttribute("dmFileMetaType", index == 0 ? "main" : "enclosure"),
                new XAttribute("dmFileDescr", attachment.FileName),
                new XElement(Isds + "dmEncodedContent", Convert.ToBase64String(attachment.Content))));
        }

        return files;
    }

    private static XElement Nil(string name) => new(Isds + name, new XAttribute(Xsi + "nil", "true"));

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string Text(XElement? element) => element?.Value.Trim() ?? "";

    private static string StatusMessage(XElement response, string statusElement)
        => Text(response.Element(Isds + statusElement)?.Element(Isds + $"{statusElement}Message"));

    private async Task<XElement> CallAsync(
        string url,
        IsdsCredentials credentials,
        XElement body,
        string statusElement,
        CancellationToken cancellationToken)
    {
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XElement(Soap + "Body", body)));

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Login}:{credentials.Password}")));
        // WSDL má soapAction="", ale hlavička musí být přítomná (SOAP 1.1).
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Požadavek mohl dorazit a být zpracován – odpověď se jen neztratila cestou zpět.
            throw new IsdsException($"Spojení s ISDS selhalo: {exception.Message}", inner: exception)
            {
                IsOutcomeUnknown = true
            };
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new IsdsException(
                    "ISDS odmítl přihlašovací údaje. Ověřte jméno a heslo do datové schránky; schránky zabezpečené jednorázovým heslem (SMS/TOTP) nebo certifikátem tato aplikace nepodporuje.",
                    response.StatusCode.ToString())
                {
                    IsAuthenticationFailure = true
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                // 4xx je odmítnutí ještě před zpracováním, 5xx může nastat i po něm – u odeslání
                // zprávy proto 5xx nepovažujeme za jistotu, že podání nevzniklo.
                throw new IsdsException($"ISDS vrátil chybu HTTP {(int)response.StatusCode}.", response.StatusCode.ToString())
                {
                    IsOutcomeUnknown = (int)response.StatusCode >= 500
                };
            }

            return ParseResponse(content, statusElement);
        }
    }

    public static XElement ParseResponse(string soapResponse, string statusElement)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(soapResponse);
        }
        catch (XmlException exception)
        {
            // Nečitelná odpověď neříká nic o tom, zda server požadavek provedl.
            throw new IsdsException("Odpověď ISDS se nepodařilo přečíst.", inner: exception) { IsOutcomeUnknown = true };
        }

        // Odpověď bez SOAP Body se nedá vyhodnotit vůbec – neříká, jestli server požadavek provedl.
        var body = document.Root?.Element(Soap + "Body")
            ?? throw new IsdsException("Odpověď ISDS neobsahuje SOAP Body.") { IsOutcomeUnknown = true };

        var fault = body.Element(Soap + "Fault");
        if (fault is not null)
        {
            var reason = Text(fault.Element("faultstring")) is { Length: > 0 } text
                ? text
                : fault.Value.Trim();
            // Věcná odmítnutí hlásí ISDS stavovým kódem, ne SOAP Faultem; fault je selhání
            // infrastruktury a stejně jako HTTP 5xx nevylučuje, že se zpráva stihla vytvořit.
            throw new IsdsException($"ISDS vrátil chybu: {reason}") { IsOutcomeUnknown = true };
        }

        var payload = body.Elements().FirstOrDefault()
            ?? throw new IsdsException("Odpověď ISDS je prázdná.") { IsOutcomeUnknown = true };

        var status = payload.Element(Isds + statusElement);
        var code = Text(status?.Element(Isds + $"{statusElement}Code"));
        if (string.IsNullOrEmpty(code))
        {
            // Bez stavového kódu nevíme, jak server požadavek vyhodnotil – nesmíme to brát jako
            // odmítnutí, jinak by se odeslání opakovalo a podání by u úřadu leželo dvakrát.
            throw new IsdsException("Odpověď ISDS neobsahuje stav zpracování.") { IsOutcomeUnknown = true };
        }

        if (code != SuccessStatus)
        {
            var message = Text(status?.Element(Isds + $"{statusElement}Message"));
            throw new IsdsException(
                string.IsNullOrWhiteSpace(message)
                    ? $"ISDS odmítl požadavek (kód {code})."
                    : $"ISDS odmítl požadavek (kód {code}): {message}",
                code);
        }

        return payload;
    }
}
