using Dph.Core.Isds;

namespace Dph.App.Tests;

// Zaznamenává, co by šlo do datové schránky, a vrací připravené odpovědi.
public sealed class FakeIsdsClient : IIsdsClient
{
    public List<(string Recipient, string Annotation, string Reference, IReadOnlyList<IsdsAttachment> Attachments)> SentMessages { get; } = [];
    public List<string> SignedSentDownloads { get; } = [];
    public List<string> DeliveryDownloads { get; } = [];

    public IsdsOwner Owner { get; set; } = new("abc1234", "Testovací poplatník", "FO");
    public Func<int, string> MessageIdFactory { get; set; } = index => $"10000{index}";
    public Exception? CreateMessageError { get; set; }
    public Exception? OwnerError { get; set; }
    public byte[]? SignedSentMessage { get; set; } = [1, 2, 3];
    public byte[]? SignedDeliveryInfo { get; set; } = [4, 5, 6];
    public Exception? DeliveryInfoError { get; set; }
    // Umožní testu pozdržet odesílání a sáhnout aplikaci pod ruku.
    public Func<Task>? BeforeCreateMessage { get; set; }
    public Func<Task>? BeforeGetOwner { get; set; }

    public async Task<IsdsOwner> GetOwnerAsync(IsdsCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (BeforeGetOwner is not null)
        {
            await BeforeGetOwner();
        }

        return OwnerError is null ? Owner : throw OwnerError;
    }

    // Zprávy, které ISDS považuje za odeslané. Test sem přidá záznam, když simuluje ztracenou
    // odpověď – zpráva u úřadu vznikla, ale aplikace se to nedozvěděla.
    public List<IsdsSentMessageInfo> SentMessageLog { get; } = [];
    public Exception? SentMessageListError { get; set; }

    public async Task<IsdsSentMessage> CreateMessageAsync(
        IsdsCredentials credentials,
        string recipientDataBoxId,
        string annotation,
        string senderReference,
        IReadOnlyList<IsdsAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        if (BeforeCreateMessage is not null)
        {
            await BeforeCreateMessage();
        }

        return await CreateMessageCoreAsync(recipientDataBoxId, annotation, senderReference, attachments);
    }

    private Task<IsdsSentMessage> CreateMessageCoreAsync(
        string recipientDataBoxId,
        string annotation,
        string senderReference,
        IReadOnlyList<IsdsAttachment> attachments)
    {
        if (CreateMessageError is not null)
        {
            return Task.FromException<IsdsSentMessage>(CreateMessageError);
        }

        SentMessages.Add((recipientDataBoxId, annotation, senderReference, attachments));
        var messageId = MessageIdFactory(SentMessages.Count);
        SentMessageLog.Add(new IsdsSentMessageInfo(messageId, senderReference, recipientDataBoxId));
        return Task.FromResult(new IsdsSentMessage(messageId, "OK"));
    }

    public Task<IReadOnlyList<IsdsSentMessageInfo>> GetSentMessagesAsync(
        IsdsCredentials credentials,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
        => SentMessageListError is null
            ? Task.FromResult<IReadOnlyList<IsdsSentMessageInfo>>(SentMessageLog.ToArray())
            : Task.FromException<IReadOnlyList<IsdsSentMessageInfo>>(SentMessageListError);

    public Task<byte[]?> DownloadSignedSentMessageAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
    {
        SignedSentDownloads.Add(messageId);
        return Task.FromResult(SignedSentMessage);
    }

    public Task<byte[]?> DownloadSignedDeliveryInfoAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
    {
        DeliveryDownloads.Add(messageId);
        return DeliveryInfoError is null
            ? Task.FromResult(SignedDeliveryInfo)
            : Task.FromException<byte[]?>(DeliveryInfoError);
    }
}

public sealed class InMemoryCredentialStore(IsdsCredentials? initial = null) : IIsdsCredentialStore
{
    public IsdsCredentials? Stored { get; private set; } = initial;

    public int ClearCount { get; private set; }

    public bool HasCredentials => Stored is not null;

    public IsdsCredentials? Load() => Stored;

    public void Save(IsdsCredentials credentials) => Stored = credentials;

    public void Clear()
    {
        Stored = null;
        ClearCount++;
    }
}
