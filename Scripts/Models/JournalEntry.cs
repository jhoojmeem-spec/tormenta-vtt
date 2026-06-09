using System;
using Godot;

namespace TormentaVTT.Models
{
    /// <summary>GM note — NPC description, location, lore, session recap, etc.</summary>
    public sealed class JournalEntry
    {
        public string Id                  { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Title               { get; set; } = "Nova Anotação";
        public string Content             { get; set; } = "";
        public string Category            { get; set; } = "Geral"; // NPC, Local, Lore, Sessão
        public bool   IsVisibleToPlayers  { get; set; } = false;

        public Godot.Collections.Dictionary ToDictionary() => new()
        {
            ["id"]       = Id,
            ["title"]    = Title,
            ["content"]  = Content,
            ["category"] = Category,
            ["visible"]  = IsVisibleToPlayers
        };

        public static JournalEntry FromDictionary(Godot.Collections.Dictionary d) => new()
        {
            Id                 = d.GetValueOrDefault("id",       Guid.NewGuid().ToString("N")[..8]).ToString(),
            Title              = d.GetValueOrDefault("title",    "").ToString(),
            Content            = d.GetValueOrDefault("content",  "").ToString(),
            Category           = d.GetValueOrDefault("category", "Geral").ToString(),
            IsVisibleToPlayers = d.TryGetValue("visible", out var v) && v.AsBool()
        };
    }

    /// <summary>
    /// A piece of information the GM can share with players mid-session.
    /// When <see cref="IsRevealedToPlayers"/> is true, players receive it
    /// via the network sync.
    /// </summary>
    public sealed class Handout
    {
        public string Id                   { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Title                { get; set; } = "Novo Handout";
        public string Content              { get; set; } = "";
        public bool   IsRevealedToPlayers  { get; set; } = false;

        public Godot.Collections.Dictionary ToDictionary() => new()
        {
            ["id"]       = Id,
            ["title"]    = Title,
            ["content"]  = Content,
            ["revealed"] = IsRevealedToPlayers
        };

        public static Handout FromDictionary(Godot.Collections.Dictionary d) => new()
        {
            Id                  = d.GetValueOrDefault("id",    Guid.NewGuid().ToString("N")[..8]).ToString(),
            Title               = d.GetValueOrDefault("title", "").ToString(),
            Content             = d.GetValueOrDefault("content","").ToString(),
            IsRevealedToPlayers = d.TryGetValue("revealed", out var v) && v.AsBool()
        };
    }
}
