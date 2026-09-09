using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Dph.Core.Isds;

namespace Dph.Core.Tests;

public sealed class IsdsClientTests
{
    private static readonly XNamespace Isds = "http://isds.czechpoint.cz/v20";

    [Fact]
    public async Task Create_message_builds_envelope_and_base64_attachment()
    {
        var handler = new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmID>987654321</dmID>
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>Provedeno</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        var result = await client.CreateMessageAsync(
            new IsdsCredentials("uzivatel", "tajne"),
            "7nyn2d9",
            "Řádné přiznání k DPH za 06/2026",
            "DPH-7-abc",
            [new IsdsAttachment("2026-06_DPHDP_podani.xml", "<DPHDP3/>"u8.ToArray())]);

        Assert.Equal("987654321", result.MessageId);
        Assert.Equal("https://ws1.datovka.gov.cz/DS/dz", handler.RequestUri!.ToString());
        Assert.Equal("Basic", handler.AuthScheme);
        Assert.Equal("uzivatel:tajne", Encoding.UTF8.GetString(Convert.FromBase64String(handler.AuthParameter!)));

        var request = XDocument.Parse(handler.RequestBody!);
        var envelope = request.Descendants(Isds + "dmEnvelope").Single();
        Assert.Equal("7nyn2d9", envelope.Element(Isds + "dbIDRecipient")!.Value);
        Assert.Equal("Řádné přiznání k DPH za 06/2026", envelope.Element(Isds + "dmAnnotation")!.Value);

        var file = request.Descendants(Isds + "dmFile").Single();
        Assert.Equal("main", file.Attribute("dmFileMetaType")!.Value);
        Assert.Equal("application/xml", file.Attribute("dmMimeType")!.Value);
        Assert.Equal("2026-06_DPHDP_podani.xml", file.Attribute("dmFileDescr")!.Value);
        Assert.Equal("<DPHDP3/>", Encoding.UTF8.GetString(Convert.FromBase64String(file.Element(Isds + "dmEncodedContent")!.Value)));
    }

    [Fact]
    public async Task Create_message_sends_the_whole_envelope_group_in_schema_order()
    {
        var handler = new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmID>1</dmID>
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        await client.CreateMessageAsync(new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "DPH-1-ref", [new IsdsAttachment("a.xml", [1])]);

        var envelope = XDocument.Parse(handler.RequestBody!).Descendants(Isds + "dmEnvelope").Single();
        Assert.Equal(
            [
                "dmSenderOrgUnit", "dmSenderOrgUnitNum", "dbIDRecipient", "dmRecipientOrgUnit", "dmRecipientOrgUnitNum",
                "dmToHands", "dmAnnotation", "dmRecipientRefNumber", "dmSenderRefNumber", "dmRecipientIdent",
                "dmSenderIdent", "dmLegalTitleLaw", "dmLegalTitleYear", "dmLegalTitleSect", "dmLegalTitlePar",
                "dmLegalTitlePoint", "dmPersonalDelivery", "dmAllowSubstDelivery"
            ],
            envelope.Elements().Select(x => x.Name.LocalName));
    }

    [Fact]
    public async Task Second_attachment_is_an_enclosure()
    {
        var handler = new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmID>1</dmID><dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        await client.CreateMessageAsync(new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "DPH-1-ref",
            [new IsdsAttachment("a.xml", [1]), new IsdsAttachment("b.xml", [2])]);

        Assert.Equal(
            ["main", "enclosure"],
            XDocument.Parse(handler.RequestBody!).Descendants(Isds + "dmFile").Select(x => x.Attribute("dmFileMetaType")!.Value));
    }

    [Fact]
    public async Task Rejects_a_recipient_that_is_not_a_data_box_id()
    {
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok("<x/>"))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "451", "Věc", "DPH-1-ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.Contains("451", exception.Message);
    }

    [Fact]
    public async Task Downloads_signed_sent_message_and_delivery_info_from_their_endpoints()
    {
        var payload = Convert.ToBase64String("ZFO"u8.ToArray());
        var handler = new StubHandler(Ok($"""
            <SignedMessageDownloadResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmSignature>{payload}</dmSignature>
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </SignedMessageDownloadResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));
        var credentials = new IsdsCredentials("u", "p");

        var message = await client.DownloadSignedSentMessageAsync(credentials, "42");
        Assert.Equal("ZFO"u8.ToArray(), message);
        Assert.Equal("https://ws1.datovka.gov.cz/DS/dz", handler.RequestUri!.ToString());
        Assert.Equal("42", XDocument.Parse(handler.RequestBody!).Descendants(Isds + "dmID").Single().Value);

        await client.DownloadSignedDeliveryInfoAsync(credentials, "42");
        Assert.Equal("https://ws1.datovka.gov.cz/DS/dx", handler.RequestUri!.ToString());
        Assert.Equal("GetSignedDeliveryInfo", XDocument.Parse(handler.RequestBody!).Root!
            .Descendants().First(x => x.Name.Namespace == Isds).Name.LocalName);
    }

    [Fact]
    public async Task Missing_signature_reads_as_not_available_yet()
    {
        var handler = new StubHandler(Ok("""
            <GetSignedDeliveryInfoResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </GetSignedDeliveryInfoResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        Assert.Null(await client.DownloadSignedDeliveryInfoAsync(new IsdsCredentials("u", "p"), "42"));
    }

    [Fact]
    public async Task Reads_owner_name_from_first_and_last_name_when_firm_name_is_empty()
    {
        var handler = new StubHandler(Ok("""
            <GetOwnerInfoFromLoginResponse xmlns="http://isds.czechpoint.cz/v20">
              <dbOwnerInfo>
                <dbID>abc1234</dbID>
                <dbType>FO</dbType>
                <pnFirstName>Bohdan</pnFirstName>
                <pnLastName>Koudelka</pnLastName>
                <firmName xsi:nil="true" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"/>
              </dbOwnerInfo>
              <dbStatus><dbStatusCode>0000</dbStatusCode><dbStatusMessage>OK</dbStatusMessage></dbStatus>
            </GetOwnerInfoFromLoginResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        var owner = await client.GetOwnerAsync(new IsdsCredentials("u", "p"));

        Assert.Equal("abc1234", owner.DataBoxId);
        Assert.Equal("Bohdan Koudelka", owner.Name);
        Assert.Equal("FO", owner.BoxType);
        Assert.Equal("https://ws1.datovka.gov.cz/DS/DsManage", handler.RequestUri!.ToString());
    }

    [Fact]
    public async Task Lists_sent_messages_with_their_sender_reference()
    {
        var handler = new StubHandler(Ok("""
            <GetListOfSentMessagesResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmRecords>
                <dmRecord>
                  <dmID>987654321</dmID>
                  <dbIDRecipient>7nyn2d9</dbIDRecipient>
                  <dmSenderRefNumber>DPH-7-abc</dmSenderRefNumber>
                </dmRecord>
                <dmRecord>
                  <dmID>987654322</dmID>
                  <dbIDRecipient>7nyn2d9</dbIDRecipient>
                  <dmSenderRefNumber>DPH-8-def</dmSenderRefNumber>
                </dmRecord>
              </dmRecords>
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </GetListOfSentMessagesResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        var sent = await client.GetSentMessagesAsync(
            new IsdsCredentials("u", "p"),
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(["DPH-7-abc", "DPH-8-def"], sent.Select(x => x.SenderReference));
        Assert.Equal("987654321", sent[0].MessageId);
        Assert.Equal("7nyn2d9", sent[0].RecipientDataBoxId);
        Assert.Equal("https://ws1.datovka.gov.cz/DS/dx", handler.RequestUri!.ToString());

        var request = XDocument.Parse(handler.RequestBody!).Descendants(Isds + "GetListOfSentMessages").Single();
        Assert.StartsWith("2026-06-01T08:00:00", request.Element(Isds + "dmFromTime")!.Value);
        Assert.StartsWith("2026-06-01T09:00:00", request.Element(Isds + "dmToTime")!.Value);
        Assert.Equal("-1", request.Element(Isds + "dmStatusFilter")!.Value); // bez filtru na stav
    }

    [Fact]
    public async Task Accepted_message_without_an_id_leaves_the_outcome_unknown()
    {
        // Stav 0000 = ISDS podání přijalo. Chybějící dmID nesmí vypadat jako neúspěch, jinak by
        // se podání odeslalo podruhé.
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>Provedeno</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
        Assert.False(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task Response_without_a_status_leaves_the_outcome_unknown()
    {
        // Ořezaná odpověď (proxy, nedočtené tělo) neříká, jak server požadavek vyhodnotil.
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmID>987654321</dmID>
            </CreateMessageResponse>
            """))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Response_without_a_soap_body_leaves_the_outcome_unknown()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """<?xml version="1.0"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"/>""",
                Encoding.UTF8,
                "text/xml")
        });
        var client = new IsdsClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Soap_fault_leaves_the_outcome_unknown()
    {
        // Věcná odmítnutí chodí stavovým kódem; fault je selhání infrastruktury a nevylučuje,
        // že zpráva u úřadu vznikla.
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok("""
            <soap:Fault xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <faultcode>soap:Server</faultcode>
              <faultstring>Internal error</faultstring>
            </soap:Fault>
            """))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
        Assert.Contains("Internal error", exception.Message);
    }

    [Fact]
    public async Task Blank_status_code_leaves_the_outcome_unknown()
    {
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmStatus><dmStatusCode></dmStatusCode><dmStatusMessage></dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Empty_soap_body_leaves_the_outcome_unknown()
    {
        var client = new IsdsClient(new HttpClient(new StubHandler(Ok(""))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Sent_list_is_paged_until_the_server_returns_a_short_page()
    {
        // Zpráva, kterou hledáme, leží až za prvních 1000 záznamů.
        var handler = new PagingHandler(totalRecords: 2500);
        var client = new IsdsClient(new HttpClient(handler));

        var sent = await client.GetSentMessagesAsync(new IsdsCredentials("u", "p"), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        Assert.Equal(2500, sent.Count);
        Assert.Equal([1, 1001, 2001], handler.RequestedOffsets);
        Assert.Contains(sent, x => x.SenderReference == "ref-2499");
    }

    [Fact]
    public async Task A_list_that_never_ends_is_reported_as_unverifiable_rather_than_incomplete()
    {
        // Kdyby server stránkování ignoroval, neúplný seznam by svedl k závěru „zpráva neexistuje“.
        var client = new IsdsClient(new HttpClient(new PagingHandler(totalRecords: int.MaxValue)));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.GetSentMessagesAsync(
            new IsdsCredentials("u", "p"), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Empty_sent_list_is_not_an_error()
    {
        var handler = new StubHandler(Ok("""
            <GetListOfSentMessagesResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </GetListOfSentMessagesResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        Assert.Empty(await client.GetSentMessagesAsync(new IsdsCredentials("u", "p"), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Message_carries_the_sender_reference_for_later_reconciliation()
    {
        var handler = new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmID>1</dmID><dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """));
        var client = new IsdsClient(new HttpClient(handler));

        await client.CreateMessageAsync(new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "DPH-42-cafe", [new IsdsAttachment("a.xml", [1])]);

        var envelope = XDocument.Parse(handler.RequestBody!).Descendants(Isds + "dmEnvelope").Single();
        Assert.Equal("DPH-42-cafe", envelope.Element(Isds + "dmSenderRefNumber")!.Value);
    }

    // Rozlišení „server odmítl“ od „nevíme, co se stalo“ rozhoduje, jestli se smí poslat znovu.
    [Fact]
    public async Task Connection_failure_leaves_the_outcome_unknown()
    {
        var client = new IsdsClient(new HttpClient(new ThrowingHandler(new HttpRequestException("network is down"))));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));

        Assert.True(exception.IsOutcomeUnknown);
    }

    [Fact]
    public async Task Server_error_leaves_the_outcome_unknown_but_a_status_code_does_not()
    {
        var serverError = new IsdsClient(new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("")
        })));
        var unknown = await Assert.ThrowsAsync<IsdsException>(() => serverError.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));
        Assert.True(unknown.IsOutcomeUnknown);

        // Stavový kód znamená, že ISDS požadavek zpracovalo a odmítlo – zpráva nevznikla.
        var rejected = new IsdsClient(new HttpClient(new StubHandler(Ok("""
            <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
              <dmStatus><dmStatusCode>1214</dmStatusCode><dmStatusMessage>Schránka je znepřístupněna</dmStatusMessage></dmStatus>
            </CreateMessageResponse>
            """))));
        var definite = await Assert.ThrowsAsync<IsdsException>(() => rejected.CreateMessageAsync(
            new IsdsCredentials("u", "p"), "7nyn2d9", "Věc", "ref", [new IsdsAttachment("a.xml", [1])]));
        Assert.False(definite.IsOutcomeUnknown);
        Assert.Equal("1214", definite.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_is_reported_as_an_authentication_failure()
    {
        var client = new IsdsClient(new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("")
        })));

        var exception = await Assert.ThrowsAsync<IsdsException>(() => client.GetOwnerAsync(new IsdsCredentials("u", "bad")));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Fact]
    public void Non_zero_status_code_carries_the_isds_message()
    {
        var exception = Assert.Throws<IsdsException>(() => IsdsClient.ParseResponse("""
            <?xml version="1.0"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <CreateMessageResponse xmlns="http://isds.czechpoint.cz/v20">
                  <dmStatus>
                    <dmStatusCode>1214</dmStatusCode>
                    <dmStatusMessage>Datová schránka příjemce je znepřístupněna</dmStatusMessage>
                  </dmStatus>
                </CreateMessageResponse>
              </soap:Body>
            </soap:Envelope>
            """, "dmStatus"));

        Assert.Equal("1214", exception.StatusCode);
        Assert.Contains("znepřístupněna", exception.Message);
    }

    [Fact]
    public void Soap_fault_is_surfaced_with_its_reason()
    {
        var exception = Assert.Throws<IsdsException>(() => IsdsClient.ParseResponse("""
            <?xml version="1.0"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <soap:Fault>
                  <faultcode>soap:Server</faultcode>
                  <faultstring>Internal error</faultstring>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """, "dmStatus"));

        Assert.Contains("Internal error", exception.Message);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>{body}</soap:Body></soap:Envelope>
            """, Encoding.UTF8, "text/xml")
    };

    // Server se stránkovaným seznamem odeslaných zpráv.
    private sealed class PagingHandler(int totalRecords) : HttpMessageHandler
    {
        private const int PageSize = 1000;

        public List<int> RequestedOffsets { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var document = XDocument.Parse(body);
            var offset = int.Parse(document.Descendants(Isds + "dmOffset").Single().Value);
            RequestedOffsets.Add(offset);

            var remaining = totalRecords == int.MaxValue ? PageSize : Math.Max(0, Math.Min(PageSize, totalRecords - offset + 1));
            var records = string.Concat(Enumerable.Range(offset, remaining).Select(i => $"""
                <dmRecord><dmID>{i}</dmID><dbIDRecipient>7nyn2d9</dbIDRecipient><dmSenderRefNumber>ref-{i - 1}</dmSenderRefNumber></dmRecord>
                """));

            return Ok($"""
                <GetListOfSentMessagesResponse xmlns="http://isds.czechpoint.cz/v20">
                  <dmRecords>{records}</dmRecords>
                  <dmStatus><dmStatusCode>0000</dmStatusCode><dmStatusMessage>OK</dmStatusMessage></dmStatus>
                </GetListOfSentMessagesResponse>
                """);
        }
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(error);
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? AuthScheme { get; private set; }
        public string? AuthParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthScheme = request.Headers.Authorization?.Scheme;
            AuthParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            // Odpověď se v testu čte opakovaně (dva požadavky na jednoho klienta).
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(await response.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, "text/xml")
            };
        }
    }
}
