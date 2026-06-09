using System.Collections.Generic;

namespace TormentaVTT.Models
{
    /// <summary>Role of a connected participant in a session.</summary>
    public enum RoleType
    {
        GM,
        Player
    }

    /// <summary>Represents one connected player (or the GM) in the current session.</summary>
    public sealed class PlayerSession
    {
        public string       Id             { get; set; } = "";
        public string       DisplayName    { get; set; } = "Jogador";
        public RoleType     Role           { get; set; } = RoleType.Player;
        public List<string> OwnedTokenIds  { get; set; } = new();
        public bool         IsConnected    { get; set; } = true;

        /// <summary>GM can do anything; players can only edit/move their own tokens.</summary>
        public bool CanControlToken(string tokenId) =>
            Role == RoleType.GM || OwnedTokenIds.Contains(tokenId);

        public bool IsGM => Role == RoleType.GM;
    }
}
