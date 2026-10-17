using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// Chaos mode: private whispers and secrets addressed to this player only, newest last.
public sealed partial class GameSession
{
    public const int MaxPrivateMessages = 5;
    private readonly List<PrivateMessageDto> _private = [];
    public IReadOnlyList<PrivateMessageDto> PrivateMessages => _private;

    public void AddPrivateMessage(PrivateMessageDto message)
    {
        if (string.IsNullOrWhiteSpace(message.Text)) return;
        _private.Add(message);
        if (_private.Count > MaxPrivateMessages) _private.RemoveAt(0);
        Changed?.Invoke();
    }
}
