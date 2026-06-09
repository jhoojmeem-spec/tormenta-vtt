using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TormentaVTT.Network
{
    // ── Message type enum ──────────────────────────────────────────────────────
    public enum NetMsgType
    {
        // Session
        PlayerHello,    // client → host: introduce self + request state
        PlayerJoined,   // host → all: broadcast new player
        PlayerLeft,     // host → all: player disconnected
        RoleAssigned,   // host → client: your role + owned tokens
        // Content
        Chat,           // any → all: chat message
        // Map
        MapLoaded,      // host → all: map changed (path ref)
        // Tokens
        TokenSpawned,   // any → all: new token added
        TokenRemoved,   // any → all: token deleted
        TokenMoved,     // any → all: position update (on drop)
        TokenStats,     // host → all: HP/PM changed
        TokenOwnership, // host → all: ownership assigned
        // Combat
        CombatStarted,  // host → all: combat begin
        CombatAdvanced, // host → all: turn changed
        CombatEnded,    // host → all: combat over
        DamageApplied,  // host → all: HP reduced
        // Fog of war
        FogUpdate,      // host → all: cells revealed/hidden
        FogReset,       // host → all: full fog reset
        // Journals
        JournalShared,  // host → all: handout revealed
        // State sync
        FullStateSync,  // host → client: complete game state on join
        RequestSync,    // client → host: please send state
    }

    // ── Envelope ──────────────────────────────────────────────────────────────
    public sealed class NetMsg
    {
        [JsonPropertyName("t")] public NetMsgType T { get; set; }
        [JsonPropertyName("s")] public string S { get; set; } = ""; // sender id
        [JsonPropertyName("p")] public string P { get; set; } = ""; // payload json

        private static readonly JsonSerializerOptions _opts =
            new() { PropertyNamingPolicy = null };

        public static string Encode<TPayload>(NetMsgType type, string senderId, TPayload payload)
        {
            var p = JsonSerializer.Serialize(payload, _opts);
            var msg = new NetMsg { T = type, S = senderId, P = p };
            return JsonSerializer.Serialize(msg, _opts);
        }

        public static NetMsg? Decode(string json)
        {
            try { return JsonSerializer.Deserialize<NetMsg>(json, _opts); }
            catch { return null; }
        }

        public TPayload DecodePayload<TPayload>()
            => JsonSerializer.Deserialize<TPayload>(P, _opts)!;
    }

    // ── Payload DTOs ──────────────────────────────────────────────────────────
    public sealed class PlayerHelloPayload
    {
        public string Name { get; set; } = "";
    }

    public sealed class PlayerJoinedPayload
    {
        public string Id   { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "Player";
    }

    public sealed class PlayerLeftPayload
    {
        public string Id { get; set; } = "";
    }

    public sealed class RoleAssignedPayload
    {
        public string          Role          { get; set; } = "Player";
        public List<string>    OwnedTokenIds { get; set; } = new();
    }

    public sealed class ChatPayload
    {
        public string Sender  { get; set; } = "";
        public string Text    { get; set; } = "";
        public string MsgType { get; set; } = "Chat";
    }

    public sealed class MapLoadedPayload
    {
        public string Path { get; set; } = "";
    }

    public sealed class TokenSpawnedPayload
    {
        public string TokenJson { get; set; } = ""; // JSON of the token dict
    }

    public sealed class TokenRemovedPayload
    {
        public string Id { get; set; } = "";
    }

    public sealed class TokenMovedPayload
    {
        public string Id { get; set; } = "";
        public float  X  { get; set; }
        public float  Y  { get; set; }
    }

    public sealed class TokenStatsPayload
    {
        public string Id { get; set; } = "";
        public int    HP { get; set; }
        public int    PM { get; set; }
    }

    public sealed class TokenOwnershipPayload
    {
        public string TokenId  { get; set; } = "";
        public string OwnerId  { get; set; } = "";
    }

    public sealed class CombatStartedPayload
    {
        public List<string>             OrderIds { get; set; } = new();
        public Dictionary<string, int>  Rolls    { get; set; } = new();
        public int                      Current  { get; set; }
    }

    public sealed class CombatAdvancedPayload
    {
        public int Current { get; set; }
    }

    public sealed class DamageAppliedPayload
    {
        public string TokenId { get; set; } = "";
        public int    NewHP   { get; set; }
    }

    public sealed class FogUpdatePayload
    {
        public List<string> Cells  { get; set; } = new(); // "x,y" format
        public bool         Reveal { get; set; }
    }

    public sealed class FogResetPayload
    {
        public bool RevealAll { get; set; }
        public List<string> Cells { get; set; } = new(); // full state
    }

    public sealed class JournalSharedPayload
    {
        public string Id      { get; set; } = "";
        public string Title   { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public sealed class FullStateSyncPayload
    {
        public string CampaignJson { get; set; } = "";
        public string Role         { get; set; } = "Player";
        public string YourId       { get; set; } = "";
        public List<string> OwnedTokenIds { get; set; } = new();
    }
}
