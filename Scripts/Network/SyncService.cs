using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using TormentaVTT.Models;
using TormentaVTT.Services;

namespace TormentaVTT.Network
{
    /// <summary>
    /// Bridges NetworkService ↔ game state.
    ///
    /// Outbound: Main.cs calls Sync* methods when game state changes.
    /// Inbound:  SyncService deserialises incoming messages and fires typed events.
    ///           Events are enqueued onto the main thread via the provided queue.
    /// </summary>
    public sealed class SyncService
    {
        private readonly NetworkService _network;
        private readonly ConcurrentQueue<Action> _queue;

        public SyncService(NetworkService network, ConcurrentQueue<Action> mainThreadQueue)
        {
            _network = network;
            _queue   = mainThreadQueue;
            _network.MessageReceived += OnRawMessage;
        }

        // ── Inbound events ────────────────────────────────────────────────────
        public event Action<string, string, string>? RemoteChatReceived;      // sender, text, type
        public event Action<string, float, float>?   RemoteTokenMoved;        // id, x, y
        public event Action<string>?                 RemoteTokenRemoved;       // id
        public event Action<string>?                 RemoteTokenSpawned;       // tokenJson
        public event Action<string, int, int>?       RemoteTokenStats;         // id, hp, pm
        public event Action<string, int>?            RemoteDamageApplied;      // id, newHP
        public event Action<List<string>,
                            Dictionary<string, int>,
                            int>?                    RemoteCombatStarted;
        public event Action<int>?                    RemoteCombatAdvanced;
        public event Action?                         RemoteCombatEnded;
        public event Action<List<string>, bool>?     RemoteFogUpdate;          // cells, reveal
        public event Action<List<string>>?           RemoteFogReset;           // all revealed cells
        public event Action<string, string, string>? RemoteJournalShared;      // id, title, content
        public event Action<string>?                 RemoteFullStateSync;       // campaignJson
        public event Action<string, string, string>? RemotePlayerJoined;       // id, name, role
        public event Action<string, string>?            RemoteOwnershipChanged; // tokenId, ownerId
        public event Action<string>?                 RemotePlayerLeft;          // id
        public event Action<string, string, string>? RemoteRoleAssigned;       // role, ownedTokenIds csv

        // ── Outbound sync ─────────────────────────────────────────────────────
        public void SyncChat(string sender, string text, string msgType)
            => Send(NetMsg.Encode(NetMsgType.Chat, _network.LocalId,
                new ChatPayload { Sender = sender, Text = text, MsgType = msgType }));

        public void SyncTokenMoved(string tokenId, float x, float y)
            => Send(NetMsg.Encode(NetMsgType.TokenMoved, _network.LocalId,
                new TokenMovedPayload { Id = tokenId, X = x, Y = y }));

        public void SyncTokenSpawned(string tokenJson)
            => Send(NetMsg.Encode(NetMsgType.TokenSpawned, _network.LocalId,
                new TokenSpawnedPayload { TokenJson = tokenJson }));

        public void SyncTokenRemoved(string tokenId)
            => Send(NetMsg.Encode(NetMsgType.TokenRemoved, _network.LocalId,
                new TokenRemovedPayload { Id = tokenId }));

        public void SyncTokenStats(string tokenId, int hp, int pm)
            => Send(NetMsg.Encode(NetMsgType.TokenStats, _network.LocalId,
                new TokenStatsPayload { Id = tokenId, HP = hp, PM = pm }));

        public void SyncMapLoaded(string path)
            => Send(NetMsg.Encode(NetMsgType.MapLoaded, _network.LocalId,
                new MapLoadedPayload { Path = path }));

        public void SyncCombatStarted(List<string> orderIds, Dictionary<string, int> rolls, int current)
            => Send(NetMsg.Encode(NetMsgType.CombatStarted, _network.LocalId,
                new CombatStartedPayload { OrderIds = orderIds, Rolls = rolls, Current = current }));

        public void SyncCombatAdvanced(int current)
            => Send(NetMsg.Encode(NetMsgType.CombatAdvanced, _network.LocalId,
                new CombatAdvancedPayload { Current = current }));

        public void SyncCombatEnded()
            => Send(NetMsg.Encode(NetMsgType.CombatEnded, _network.LocalId, new { }));

        public void SyncDamage(string tokenId, int newHP)
            => Send(NetMsg.Encode(NetMsgType.DamageApplied, _network.LocalId,
                new DamageAppliedPayload { TokenId = tokenId, NewHP = newHP }));

        public void SyncFogUpdate(List<string> cells, bool reveal)
            => Send(NetMsg.Encode(NetMsgType.FogUpdate, _network.LocalId,
                new FogUpdatePayload { Cells = cells, Reveal = reveal }));

        public void SyncFogReset(List<string> allRevealedCells)
            => Send(NetMsg.Encode(NetMsgType.FogReset, _network.LocalId,
                new FogResetPayload { Cells = allRevealedCells }));

        public void SyncJournalShared(string id, string title, string content)
            => Send(NetMsg.Encode(NetMsgType.JournalShared, _network.LocalId,
                new JournalSharedPayload { Id = id, Title = title, Content = content }));

        /// <summary>Host sends full state to a newly joined client.</summary>
        public void SendFullStateTo(string clientId, string campaignJson, RoleType role,
                                    string clientOwnId, List<string> ownedTokenIds)
        {
            var p = new FullStateSyncPayload
            {
                CampaignJson  = campaignJson,
                Role          = role.ToString(),
                YourId        = clientOwnId,
                OwnedTokenIds = ownedTokenIds
            };
            var msg = NetMsg.Encode(NetMsgType.FullStateSync, _network.LocalId, p);
            _ = _network.SendToClientAsync(clientId, msg);
        }

        /// <summary>Client requests a full state dump after connecting.</summary>
        public void RequestFullState(string playerName)
        {
            var msg = NetMsg.Encode(NetMsgType.PlayerHello, _network.LocalId,
                new PlayerHelloPayload { Name = playerName });
            _ = _network.SendToHostAsync(msg);
        }

        // ── Internal ─────────────────────────────────────────────────────────
        private void Send(string json) => _ = _network.SendAsync(json);

        private void OnRawMessage(string senderId, string json)
        {
            var msg = NetMsg.Decode(json);
            if (msg == null) return;
            // Enqueue handling to main thread
            _queue.Enqueue(() => DispatchMessage(msg, senderId));
        }

        private void DispatchMessage(NetMsg msg, string rawSenderId)
        {
            try
            {
                switch (msg.T)
                {
                    case NetMsgType.Chat:
                    {
                        var p = msg.DecodePayload<ChatPayload>();
                        RemoteChatReceived?.Invoke(p.Sender, p.Text, p.MsgType);
                        break;
                    }
                    case NetMsgType.TokenMoved:
                    {
                        var p = msg.DecodePayload<TokenMovedPayload>();
                        RemoteTokenMoved?.Invoke(p.Id, p.X, p.Y);
                        break;
                    }
                    case NetMsgType.TokenSpawned:
                    {
                        var p = msg.DecodePayload<TokenSpawnedPayload>();
                        RemoteTokenSpawned?.Invoke(p.TokenJson);
                        break;
                    }
                    case NetMsgType.TokenRemoved:
                    {
                        var p = msg.DecodePayload<TokenRemovedPayload>();
                        RemoteTokenRemoved?.Invoke(p.Id);
                        break;
                    }
                    case NetMsgType.TokenStats:
                    {
                        var p = msg.DecodePayload<TokenStatsPayload>();
                        RemoteTokenStats?.Invoke(p.Id, p.HP, p.PM);
                        break;
                    }
                    case NetMsgType.DamageApplied:
                    {
                        var p = msg.DecodePayload<DamageAppliedPayload>();
                        RemoteDamageApplied?.Invoke(p.TokenId, p.NewHP);
                        break;
                    }
                    case NetMsgType.MapLoaded:
                    {
                        var p = msg.DecodePayload<MapLoadedPayload>();
                        // Map files are local — notify via chat but don't auto-load
                        RemoteChatReceived?.Invoke("Sistema", $"[Mapa alterado pelo GM: {p.Path}]", "System");
                        break;
                    }
                    case NetMsgType.CombatStarted:
                    {
                        var p = msg.DecodePayload<CombatStartedPayload>();
                        RemoteCombatStarted?.Invoke(p.OrderIds, p.Rolls, p.Current);
                        break;
                    }
                    case NetMsgType.CombatAdvanced:
                    {
                        var p = msg.DecodePayload<CombatAdvancedPayload>();
                        RemoteCombatAdvanced?.Invoke(p.Current);
                        break;
                    }
                    case NetMsgType.CombatEnded:
                        RemoteCombatEnded?.Invoke();
                        break;
                    case NetMsgType.FogUpdate:
                    {
                        var p = msg.DecodePayload<FogUpdatePayload>();
                        RemoteFogUpdate?.Invoke(p.Cells, p.Reveal);
                        break;
                    }
                    case NetMsgType.FogReset:
                    {
                        var p = msg.DecodePayload<FogResetPayload>();
                        RemoteFogReset?.Invoke(p.Cells);
                        break;
                    }
                    case NetMsgType.JournalShared:
                    {
                        var p = msg.DecodePayload<JournalSharedPayload>();
                        RemoteJournalShared?.Invoke(p.Id, p.Title, p.Content);
                        break;
                    }
                    case NetMsgType.FullStateSync:
                    {
                        var p = msg.DecodePayload<FullStateSyncPayload>();
                        // Fire role first so Main.cs can set up before loading campaign
                        var ownedCsv = string.Join(",", p.OwnedTokenIds);
                        RemoteRoleAssigned?.Invoke(p.Role, ownedCsv, p.YourId);
                        RemoteFullStateSync?.Invoke(p.CampaignJson);
                        break;
                    }
                    case NetMsgType.PlayerHello:
                    {
                        // Only the host receives this (client → host)
                        var p = msg.DecodePayload<PlayerHelloPayload>();
                        RemotePlayerJoined?.Invoke(rawSenderId, p.Name, "Player");
                        break;
                    }
                    case NetMsgType.PlayerJoined:
                    {
                        var p = msg.DecodePayload<PlayerJoinedPayload>();
                        RemotePlayerJoined?.Invoke(p.Id, p.Name, p.Role);
                        break;
                    }
                    case NetMsgType.PlayerLeft:
                    {
                        var p = msg.DecodePayload<PlayerLeftPayload>();
                        RemotePlayerLeft?.Invoke(p.Id);
                        break;
                    }
                    case NetMsgType.TokenOwnership:
                    {
                        var p = msg.DecodePayload<TokenOwnershipPayload>();
                        RemoteOwnershipChanged?.Invoke(p.TokenId, p.OwnerId);
                        break;
                    }
                }
            }
            catch { /* malformed payload — ignore */ }
        }
    }
}
